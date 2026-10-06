using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Золото забега и «Уйти» (экономика 06.10, план «Лагерь 06–10.10», S.2 и S.9).
    ///
    /// Зачистка арены N — 2×N, уровень босса — 30, «Сложно» удваивает золото уровня,
    /// элита — 25, разбор навыка — плоские 10. Процент «Звонкой монеты» — от суммы.
    /// Уйти можно только с экранов награды, замены и пути: в бою и по дороге к выходу
    /// команда ничего не делает.
    /// </summary>
    public sealed class RunEconomyTests
    {
        private static InputFrame Command(RunCommand command) => new InputFrame { Command = (byte)command };

        /// <summary>Восемь простых арен и босс (FormBaselineScenarios): золото уровня без шаблонов встреч.</summary>
        private static RiftRun ArenaRun(ulong seed = 11)
        {
            RiftRun run = FormBaselineScenarios.NewArenaRun(seed);
            run.StartRun();
            return run;
        }

        /// <summary>Первая карточка; способность при полной панели встаёт заменой — без золота разбора.</summary>
        private static void TakeReward(RiftRun run)
        {
            run.Step(FormBaselineScenarios.Choice(0));
            if (run.Phase == RunPhase.ReplacingAbility) run.Step(Command(RunCommand.ReplaceSlot2));
        }

        private static void FillPanel(RiftRun run)
        {
            for (int slot = 1; slot < RunLoadout.Slots; slot++) run.Loadout.Put(slot, slot);
            run.ApplyLoadout();
        }

        /// <summary>Пачки как у стенда стали: обычные хранители, у выхода — две элиты. Без арен — без золота зачистки.</summary>
        private static RiftRun EliteRun(ulong seed)
        {
            var normal = new EncounterPack(1, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 1, 1) });
            var elite = new EncounterPack(2, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 2, 2, elite: true) });
            var settings = new EncounterSettings(new[] { normal }, new[] { normal }, new[] { normal }, new[] { elite },
                1, 0, 100, Fix64.FromInt(5));
            var location = new LocationDefinition(StableId.Of("location.test-run-economy"), PrototypeContent.Modules(),
                new[] { new RiftLevelSettings(12, 1, 0, 0, 0, 0, 100, settings) });
            var run = new RiftRun(new Simulation(seed, 512), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            run.StartRun();
            for (int id = 1; id < run.Sim.Entities.Count; id++) run.Sim.Entities.NextAttackTick[id] = int.MaxValue;
            return run;
        }

        /// <summary>Поджечь насмерть от имени героя: смерть идёт штатным путём шага боя (событие Death).</summary>
        private static void Burn(RiftRun run, int id)
            => run.Sim.Statuses.ApplyBurn(id, Fix64.FromInt(10000000), 1, Simulation.PlayerId, -1);

        private static int LiveElites(RiftRun run, int[] into)
        {
            int count = 0;
            for (int id = 1; id < run.Sim.Entities.Count; id++)
                if (run.Sim.Entities.Alive[id] && run.Encounters.IsElite(id) && id != run.BossId) into[count++] = id;
            return count;
        }

        // ---- золото ----

        [Test]
        public void ClearGold_IsTwiceTheArenaNumber_HardDoubles()
        {
            for (int n = 1; n <= 8; n++)
            {
                Assert.AreEqual(2 * n, RunEconomy.ArenaClearGold(n, false, false));
                Assert.AreEqual(4 * n, RunEconomy.ArenaClearGold(n, true, false));
            }

            RiftRun run = ArenaRun();
            Assert.AreEqual(2, run.CurrentRoute.BonusGold, "А1 — 2");
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
            Assert.AreEqual(2, run.Gold);
            for (int depth = 1; depth <= 4; depth++)
            {
                TakeReward(run);
                Assert.AreEqual(RunPhase.ChoosingRoute, run.Phase);
                int next = run.Depth + 1;
                Assert.AreEqual(2 * next, run.GetRoute(0).BonusGold);
                Assert.AreEqual(2 * next, run.GetRoute(1).BonusGold);
                Assert.AreEqual(4 * next, run.GetRoute(2).BonusGold, "«Сложно» удваивает");
                int route = depth % 3;
                run.Step(Command((RunCommand)((int)RunCommand.ChooseRoute1 + route)));
                int before = run.Gold;
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                Assert.AreEqual(before + (route == 2 ? 4 * next : 2 * next), run.Gold, "арена " + next);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BossLevelClear_Pays30_Hard60(bool hard)
        {
            Assert.AreEqual(30, RunEconomy.ArenaClearGold(9, false, true));
            Assert.AreEqual(60, RunEconomy.ArenaClearGold(9, true, true));

            RiftRun run = ArenaRun(23);
            for (int depth = 1; depth < FormBaselineScenarios.ArenaCount; depth++)
            {
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                TakeReward(run);
                run.Step(Command(RunCommand.ChooseRoute1));
            }
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
            TakeReward(run);
            Assert.AreEqual(RunPhase.ChoosingRoute, run.Phase);
            Assert.AreEqual(30, run.GetRoute(0).BonusGold);
            Assert.AreEqual(30, run.GetRoute(1).BonusGold);
            Assert.AreEqual(60, run.GetRoute(2).BonusGold);

            // Временный босс-Хранитель: без вступления его можно поджечь в первый же тик.
            run.Sim.ThicketMasterBossEnabled = false;
            run.Step(Command(hard ? RunCommand.ChooseRoute3 : RunCommand.ChooseRoute1));
            Assert.Greater(run.BossId, 0);
            int before = run.Gold;
            Burn(run, run.BossId);
            run.Step(InputFrame.Empty);
            Assert.IsFalse(run.Sim.Entities.Alive[run.BossId]);
            // Подмога встаёт только при живом боссе: павший в один тик босс зачищает уровень сразу.
            Assert.AreEqual(RunPhase.SeekingExit, run.Phase);
            Assert.AreEqual(before + (hard ? 60 : 30), run.Gold, "босс помечен элитой, но платит золотом уровня, а не как элита");
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
            Assert.AreEqual(before + (hard ? 60 : 30), run.Gold);
        }

        [Test]
        public void EliteDeath_PaysTwentyFiveOnce()
        {
            RiftRun run = EliteRun(846);
            var elites = new int[8];
            Assert.AreEqual(2, LiveElites(run, elites));
            int normal = -1;
            for (int id = 1; id < run.Sim.Entities.Count && normal < 0; id++)
                if (run.Sim.Entities.Alive[id] && !run.Encounters.IsElite(id)) normal = id;
            Assert.Greater(normal, 0);

            Burn(run, normal);
            run.Step(InputFrame.Empty);
            Assert.AreEqual(0, run.Gold, "обычный враг золота не даёт");

            Burn(run, elites[0]);
            run.Step(InputFrame.Empty);
            Assert.AreEqual(RunEconomy.EliteGold, run.Gold);
            for (int t = 0; t < 20; t++) run.Step(InputFrame.Empty);
            Assert.AreEqual(RunEconomy.EliteGold, run.Gold, "одна элита — одна плата");

            Burn(run, elites[1]);
            run.Step(InputFrame.Empty);
            Assert.AreEqual(2 * RunEconomy.EliteGold, run.Gold);
        }

        [Test]
        public void Salvage_PaysFlatTen_FromReplaceAndPickupMenu()
        {
            Assert.AreEqual(10, RunEconomy.SalvageGold);

            // Экран замены: прототипный забег без золота зачистки.
            RiftRun replace = ReplaceScreen();
            Assert.AreEqual(RunEconomy.SalvageGold, replace.SalvageGold);
            int before = replace.Gold;
            replace.Step(Command(RunCommand.SalvageAbility));
            Assert.AreEqual(before + 10, replace.Gold);

            // Мини-меню над способностью с элиты при полной панели.
            for (ulong seed = 1; seed < 300; seed++)
            {
                RiftRun run = EliteRun(seed);
                FillPanel(run);
                var elites = new int[8];
                int count = LiveElites(run, elites);
                for (int i = 0; i < count; i++) Burn(run, elites[i]);
                run.Step(InputFrame.Empty);
                int drop = -1;
                for (int d = 0; d < run.DropCount && drop < 0; d++)
                    if (!run.GetDrop(d).Claimed && run.GetDrop(d).Offer.Kind == RewardKind.Ability) drop = d;
                if (drop < 0) continue;

                run.Sim.Entities.Position[Simulation.PlayerId] = run.GetDrop(drop).Position;
                run.Step(InputFrame.Empty);
                Assert.IsFalse(run.GetDrop(drop).Claimed, "полная панель: способность ждёт решения в мини-меню");
                before = run.Gold;
                run.Step(Command(RunCommand.PickupSalvage));
                Assert.IsTrue(run.GetDrop(drop).Claimed);
                Assert.AreEqual(before + 10, run.Gold, "сид " + seed);
                return;
            }
            Assert.Fail("за 300 сидов элита ни разу не уронила способность");
        }

        private static RiftRun ReplaceScreen()
        {
            for (ulong seed = 1; seed < 400; seed++)
            {
                RiftRun run = FormBaselineScenarios.NewPrototypeRun(seed);
                run.StartRun();
                FillPanel(run);
                if (!FormBaselineScenarios.ClearToReward(run)) continue;
                for (int card = 0; card < RiftRun.RewardChoices; card++)
                {
                    if (run.GetOffer(card).Kind != RewardKind.Ability) continue;
                    run.Step(FormBaselineScenarios.Choice(card));
                    if (run.Phase == RunPhase.ReplacingAbility) return run;
                    break;
                }
            }
            Assert.Fail("за 400 сидов не нашлось способности при полной панели");
            return null;
        }

        /// <summary>
        /// «Звонкая монета» (клятвы, T2) берёт процент от суммы найденного, а не от каждой порции:
        /// лес 72 при 115% — 82, а по порциям вышло бы 79. Сегодня GoldPercent — 100, Gold == сумме.
        /// </summary>
        [Test]
        public void GoldPercent_RoundsFromTotal()
        {
            int total = 0, perPortion = 0;
            for (int n = 1; n <= 8; n++)
            {
                int portion = RunEconomy.ArenaClearGold(n, false, false);
                total += portion;
                perPortion += RunEconomy.Percent(portion, 115);
            }
            Assert.AreEqual(72, total);
            Assert.AreEqual(82, RunEconomy.Percent(total, 115));
            Assert.AreEqual(79, perPortion, "по порциям терялось бы по единице на каждой");
            Assert.AreEqual(total, RunEconomy.Percent(total, 100));
            Assert.AreEqual(int.MaxValue / 2, RunEconomy.Percent(int.MaxValue, 50), "без переполнения в умножении");
        }

        [Test]
        public void GoodForestArithmetic_127WithOneElite_152WithTwo()
        {
            int arenas = 0;
            for (int n = 1; n <= 8; n++) arenas += RunEconomy.ArenaClearGold(n, false, false);
            int forest = arenas + RunEconomy.ArenaClearGold(9, false, true);
            Assert.AreEqual(127, forest + RunEconomy.EliteGold);
            Assert.AreEqual(152, forest + 2 * RunEconomy.EliteGold);
            Assert.AreEqual(63, RunEconomy.Percent(127, RunEconomy.DeathGoldKeptPercent), "смерть — половина, вниз");
        }

        // ---- «Уйти» ----

        [Test]
        public void Leave_IsIgnoredWhileClearingAndSeekingExit()
        {
            RiftRun run = ArenaRun(31);
            Assert.IsFalse(run.CanLeave);
            int tick = run.Sim.Tick;
            run.Step(Command(RunCommand.Leave));
            Assert.AreEqual(RunPhase.Clearing, run.Phase);
            Assert.AreEqual(RunOutcome.None, run.Outcome);
            Assert.AreEqual(tick + 1, run.Sim.Tick, "бой шагает дальше, команда просто не действует");

            EntityStore e = run.Sim.Entities;
            for (int guard = 0; guard < 4000 && run.Phase == RunPhase.Clearing; guard++)
            {
                for (int i = 0; i < e.Count; i++)
                    if (e.Side[i] != Faction.Wole) e.Alive[i] = false;
                run.Step(InputFrame.Empty);
            }
            Assert.AreEqual(RunPhase.SeekingExit, run.Phase);
            Assert.IsFalse(run.CanLeave);
            run.Step(Command(RunCommand.Leave));
            Assert.AreEqual(RunPhase.SeekingExit, run.Phase);
            Assert.AreEqual(RunOutcome.None, run.Outcome);
        }

        [Test]
        public void Leave_EndsRunFromRewardReplaceAndRouteScreens()
        {
            RiftRun reward = ArenaRun(41);
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(reward));
            Assert.IsTrue(reward.CanLeave);
            reward.Step(Command(RunCommand.Leave));
            Assert.AreEqual(RunPhase.Ended, reward.Phase);
            Assert.AreEqual(RunOutcome.Left, reward.Outcome);

            RiftRun replace = ReplaceScreen();
            Assert.IsTrue(replace.CanLeave);
            replace.Step(Command(RunCommand.Leave));
            Assert.AreEqual(RunOutcome.Left, replace.Outcome);

            RiftRun route = ArenaRun(43);
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(route));
            TakeReward(route);
            Assert.AreEqual(RunPhase.ChoosingRoute, route.Phase);
            Assert.IsTrue(route.CanLeave);
            route.Step(Command(RunCommand.Leave));
            Assert.AreEqual(RunOutcome.Left, route.Outcome);
        }
    }
}
