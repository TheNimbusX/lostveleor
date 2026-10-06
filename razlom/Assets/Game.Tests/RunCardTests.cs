using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Карточки после арены и судьба добычи. Решения владельца от 15 сентября:
    /// способность 35 / вещь 30 / талант 35, при полной панели 15 / 30 / 55;
    /// новая способность при полной панели — замена или разбор. Экономика 06.10:
    /// разбор — плоские 10, смерть отнимает вещи и половину золота, уйти можно
    /// только с экранов награды, замены и пути.
    /// </summary>
    public class RunCardTests
    {
        private static RiftRun NewRun(ulong seed, Action<RunLoadout> setup = null)
        {
            var run = new RiftRun(new Simulation(seed, 1024), PrototypeContent.Modules(),
                PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            run.StartRun();
            setup?.Invoke(run.Loadout);
            return run;
        }

        private static void ReachReward(RiftRun run)
        {
            for (int i = 0; i < run.Sim.Entities.Count; i++)
                if (run.Sim.Entities.Side[i] != Faction.Wole) run.Sim.Entities.Alive[i] = false;
            run.Step(InputFrame.Empty);
            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
        }

        private static InputFrame Command(RunCommand command) => new InputFrame { Command = (byte)command };

        private static InputFrame Choice(int card) => Command((RunCommand)((int)RunCommand.ChooseReward1 + card));

        private static void FillSabre(RunLoadout loadout)
        {
            for (int slot = 1; slot < RunLoadout.Slots; slot++) loadout.Put(slot, slot);
        }

        /// <summary>Полная панель, все таланты взяты: доступных талантов нет.</summary>
        private static void FillMaxed(RunLoadout loadout)
        {
            FillSabre(loadout);
            for (int pool = 0; pool < RunLoadout.Slots; pool++)
                while (loadout.TakeTalent(pool)) { }
        }

        /// <summary>Доли вида первой карточки на панели по сотням сидов.</summary>
        private static void MeasureFirstCard(Action<RunLoadout> setup, out float ability, out float item, out float talent)
        {
            const int runs = 400;
            int a = 0, i = 0, t = 0;
            for (ulong seed = 1; seed <= runs; seed++)
            {
                var run = NewRun(seed, setup);
                ReachReward(run);
                switch (run.GetOffer(0).Kind)
                {
                    case RewardKind.Ability: a++; break;
                    case RewardKind.Item: i++; break;
                    case RewardKind.Talent: t++; break;
                    default: Assert.Fail("вид карточки вне решения владельца: " + run.GetOffer(0).Kind); break;
                }
            }
            ability = a / (float)runs;
            item = i / (float)runs;
            talent = t / (float)runs;
        }

        /// <summary>Находит сид, где на панели есть карточка нужного вида, и возвращает её номер.</summary>
        private static int FindCard(RewardKind kind, Action<RunLoadout> setup, out RiftRun run)
        {
            for (ulong seed = 1; seed < 500; seed++)
            {
                run = NewRun(seed, setup);
                ReachReward(run);
                for (int c = 0; c < RiftRun.RewardChoices; c++)
                    if (run.GetOffer(c).Kind == kind) return c;
            }
            Assert.Fail("за 500 сидов не выпало карточки " + kind);
            run = null;
            return -1;
        }

        // ---- веса ----

        [Test]
        public void WithAFreeSlotCardsFollowThirtyFiveThirtyThirtyFive()
        {
            MeasureFirstCard(null, out float ability, out float item, out float talent);
            Assert.AreEqual(.35f, ability, .07f, "способность");
            Assert.AreEqual(.30f, item, .07f, "вещь");
            Assert.AreEqual(.35f, talent, .07f, "талант");
        }

        [Test]
        public void WithAFullPanelTalentsDominate()
        {
            MeasureFirstCard(FillSabre, out float ability, out float item, out float talent);
            Assert.AreEqual(.15f, ability, .06f, "способность");
            Assert.AreEqual(.30f, item, .07f, "вещь");
            Assert.AreEqual(.55f, talent, .07f, "талант");
        }

        // ---- выбор ----

        [Test]
        public void FullPanelAbilityWaitsForAReplacement()
        {
            int card = FindCard(RewardKind.Ability, FillSabre, out RiftRun run);
            int pool = run.GetOffer(card).PoolIndex;
            run.Loadout.TakeTalent(2);
            run.Loadout.TakeTalent(2);

            run.Step(Choice(card));
            Assert.AreEqual(RunPhase.ReplacingAbility, run.Phase);
            Assert.AreEqual(pool, run.PendingAbility);
            int tick = run.Sim.Tick;
            run.Step(InputFrame.Empty);
            Assert.AreEqual(tick, run.Sim.Tick, "бой шагал, пока игрок выбирает замену");

            run.Step(Command(RunCommand.ReplaceSlot3));

            Assert.AreEqual(pool, run.Loadout.PoolIndexAt(2));
            Assert.AreEqual(0, run.Loadout.TalentRank(2), "таланты заменённой способности остались");
            Assert.AreEqual(RunPhase.Clearing, run.Phase);
            Assert.AreEqual(2, run.Depth);
        }

        [Test]
        public void SalvagingGivesFlatTen()
        {
            int card = FindCard(RewardKind.Ability, FillSabre, out RiftRun run);
            run.Step(Choice(card));

            run.Step(Command(RunCommand.SalvageAbility));

            // Экономика 06.10: плоские 10 вместо 15 + 5 × глубина (прототип — без золота зачистки).
            Assert.AreEqual(RunEconomy.SalvageGold, run.SalvageGold);
            Assert.AreEqual(10, run.Gold);
            Assert.IsFalse(run.Loadout.Owns(run.PendingAbility));
            Assert.AreEqual(-1, run.PendingAbility);
            Assert.AreEqual(2, run.Depth);
        }

        // ---- судьба добычи ----

        private static GameSession SessionAtReward(ulong seed, RewardKind kind, bool fullPanel, out int card)
        {
            for (ulong s = seed; s < seed + 500; s++)
            {
                var session = new GameSession(s, new Camp(PrototypeContent.Items(), act: 3),
                    PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
                session.EnterRift();
                if (fullPanel) FillSabre(session.Run.Loadout);
                RiftRun run = session.Run;
                for (int i = 0; i < run.Sim.Entities.Count; i++)
                    if (run.Sim.Entities.Side[i] != Faction.Wole) run.Sim.Entities.Alive[i] = false;
                session.Step(InputFrame.Empty);
                run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
                session.Step(InputFrame.Empty);
                for (card = 0; card < RiftRun.RewardChoices; card++)
                    if (run.GetOffer(card).Kind == kind) return session;
            }
            Assert.Fail("не нашлось сессии с карточкой " + kind);
            card = -1;
            return null;
        }

        private static void Die(GameSession session)
        {
            session.Run.Sim.Entities.Alive[Simulation.PlayerId] = false;
            session.Step(InputFrame.Empty);
            Assert.AreEqual(GameMode.Summary, session.Mode);
        }

        /// <summary>
        /// Экономика 06.10: при смерти доезжает половина найденного золота (вниз от суммы),
        /// при уходе — всё. Ожидание — от Run.Gold перед концом: на экране могла уже лежать
        /// плата за элиту.
        /// </summary>
        [Test]
        public void DeathKeepsHalfRunGoldAndLeavingKeepsAll()
        {
            var died = SessionAtReward(1, RewardKind.Ability, true, out int card);
            int walletBefore = died.Camp.Money(CurrencyType.Gold);
            died.Step(Choice(card));
            died.Step(Command(RunCommand.SalvageAbility));
            int found = died.Run.Gold;
            Assert.AreEqual(RunEconomy.SalvageGold, found);
            Die(died);
            Assert.AreEqual(walletBefore + found / 2, died.Camp.Money(CurrencyType.Gold));
            Assert.AreEqual(found / 2, died.LastRun.GoldKept);
            Assert.AreEqual(found - found / 2, died.LastRun.GoldLeftBehind);

            var left = SessionAtReward(1, RewardKind.Ability, true, out card);
            walletBefore = left.Camp.Money(CurrencyType.Gold);
            left.Step(Choice(card));
            left.Step(Command(RunCommand.SalvageAbility));
            // После разбора начался следующий Разлом: в бою «Уйти» не действует.
            left.Step(Command(RunCommand.Leave));
            Assert.AreEqual(GameMode.Rift, left.Mode);
            Assert.AreEqual(RunOutcome.None, left.Run.Outcome);
            ReachRewardInSession(left);
            found = left.Run.Gold;
            left.Step(Command(RunCommand.Leave));
            Assert.AreEqual(GameMode.Summary, left.Mode);
            Assert.AreEqual(walletBefore + found, left.Camp.Money(CurrencyType.Gold));
            Assert.AreEqual(found, left.LastRun.GoldKept);
            Assert.AreEqual(0, left.LastRun.GoldLeftBehind);
        }

        private static void ReachRewardInSession(GameSession session)
        {
            RiftRun run = session.Run;
            for (int i = 0; i < run.Sim.Entities.Count; i++)
                if (run.Sim.Entities.Side[i] != Faction.Wole) run.Sim.Entities.Alive[i] = false;
            session.Step(InputFrame.Empty);
            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            session.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward, run.Phase);
        }
    }
}
