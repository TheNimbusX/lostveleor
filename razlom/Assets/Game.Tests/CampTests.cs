using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Лагерь и вход в забег.
    ///
    /// Приёмка: из лагеря можно войти в Разлом, забег кончается экраном итогов,
    /// с него есть и «повторить одним нажатием», и возврат в лагерь; добытое
    /// доезжает до сумки и переживает следующий забег.
    ///
    /// Отдельно проверяется Полигон — он существует ради одного: показать,
    /// что найденная вещь делает с уроном. Тест на это и стоит.
    /// </summary>
    public class CampTests
    {
        private const ulong Seed = 0x5A4D1EUL;

        private static ItemInstance Sword(ulong seed, short level = 20)
            => new ItemInstance(StableId.Of("base.rusty_sword"), level, ItemRarity.Rare, seed);

        private static ItemInstance Jacket(ulong seed, short level = 20)
            => new ItemInstance(StableId.Of("base.leather_jacket"), level, ItemRarity.Rare, seed);

        private static InputFrame Command(CampCommand command)
            => new InputFrame { Command = (byte)command };

        private static InputFrame Idle => InputFrame.Empty;

        /// <summary>
        /// Кадр с зажатой атакой. Автоатаки больше нет: персонаж бьёт только
        /// по приказу, и на Полигоне этот приказ отдаёт игрок, держа кнопку.
        /// </summary>
        private static InputFrame Attacking
            => new InputFrame { Flags = (byte)InputFlags.Attack };

        private static GameSession Session(ulong seed = Seed)
            => PrototypeContent.NewSession(seed);

        /// <summary>Зачищает текущий Разлом руками: проверяется петля, а не бой.</summary>
        private static void ClearRift(GameSession session)
        {
            EntityStore entities = session.Run.Sim.Entities;
            for (int i = 0; i < entities.Count; i++)
                if (entities.Side[i] != Faction.Wole) entities.Alive[i] = false;
            session.Step(Idle);
            entities.Position[Simulation.PlayerId] = session.Run.Map.ExitPoint(0);
            session.Step(Idle);
            Assert.That(session.Run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
        }

        /// <summary>Способность при полной панели разбирается: эти тесты проверяют лагерь, а не набор.</summary>
        private static void SalvageIfReplacing(GameSession session)
        {
            if (session.Mode == GameMode.Rift && session.Run.Phase == RunPhase.ReplacingAbility)
                session.Step(new InputFrame { Command = (byte)RunCommand.SalvageAbility });
        }

        /// <summary>Проходит забег до конца: зачищает Разлом, берёт награду, уходит с добычей.</summary>
        private static void PlayOneRift(GameSession session)
        {
            ClearRift(session);
            session.Step(Idle);

            var take = new InputFrame { Command = (byte)RunCommand.ChooseReward1 };
            session.Step(in take);

            var leave = new InputFrame { Command = (byte)RunCommand.Leave };
            session.Step(in leave);
        }

        // ---- приёмка ----

        [Test]
        public void Camp_UsesCombatSimulationForMovementAndAbilities()
        {
            var session = Session();
            var sim = session.ActiveSim;
            sim.SetAbility(0, AbilityDefinition.AnchorLeap(), new AbilityNode[0], 0);
            var input = InputFrame.Empty;
            input.AbilityMask = 1;
            input.Aim = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            session.Step(input);
            for (int i = 0; i < 60; i++) session.Step(InputFrame.Empty);
            Assert.Greater(sim.Entities.Position[0].X.ToFloat(), 4f);
            Assert.Greater(sim.AbilityReadyTick(0), 0);
            Assert.AreEqual(GameMode.Camp, session.Mode);
            Assert.IsNull(session.Run);
        }

        [Test]
        public void Session_StartsInCamp()
        {
            GameSession session = Session();

            Assert.AreEqual(GameMode.Camp, session.Mode);
            Assert.IsNull(session.Run, "в лагере забега нет");
            Assert.AreSame(session.CampSim, session.ActiveSim);
            Assert.AreEqual(1, session.ActiveSim.Entities.Count);
            Assert.IsTrue(session.ActiveSim.Entities.Alive[Simulation.PlayerId]);
        }

        [Test]
        public void Portal_TakesThePlayerIntoTheRift()
        {
            GameSession session = Session();
            session.Step(Command(CampCommand.EnterRift));

            Assert.AreEqual(GameMode.Rift, session.Mode);
            Assert.IsNotNull(session.Run);
            Assert.AreEqual(1, session.Run.Depth, "забег начинается с первого Разлома");
            Assert.AreEqual(1, session.RunNumber);
            Assert.AreSame(session.Run.Sim, session.ActiveSim, "рисуется забег");
        }

        [Test]
        public void PauseMenu_ReturnToCamp_AbandonsTheActiveRiftImmediately()
        {
            GameSession session = Session();
            session.Step(Command(CampCommand.EnterRift));
            int generationBefore = session.Generation;

            session.ReturnToCamp();

            Assert.AreEqual(GameMode.Camp, session.Mode);
            Assert.IsNull(session.Run, "покинутый забег больше не должен тикать за меню");
            Assert.AreSame(session.CampSim, session.ActiveSim);
            Assert.AreEqual(1, session.ActiveSim.Entities.Count);
            Assert.AreEqual(generationBefore + 1, session.Generation,
                "представление обязано пересобраться под лагерь");
        }

        [Test]
        public void RunEnd_LeadsToTheSummaryScreen()
        {
            GameSession session = Session();
            session.Step(Command(CampCommand.EnterRift));
            PlayOneRift(session);

            Assert.AreEqual(GameMode.Summary, session.Mode);
            Assert.AreEqual(RunOutcome.Left, session.LastRun.Outcome);
            Assert.AreEqual(1, session.LastRun.RiftsCleared);
        }

        [Test]
        public void SameSessionSeed_GivesTheSameChainOfRuns()
        {
            ulong[] Chain(ulong seed)
            {
                GameSession session = Session(seed);
                var seeds = new ulong[3];

                session.Step(Command(CampCommand.EnterRift));
                seeds[0] = session.LastRunSeed;

                for (int i = 1; i < seeds.Length; i++)
                {
                    PlayOneRift(session);
                    session.Step(Command(CampCommand.RepeatRift));
                    seeds[i] = session.LastRunSeed;
                }
                return seeds;
            }

            // Воспроизводимой обязана быть вся цепочка, а не каждый забег
            // по отдельности: на этом потом стоит проверка топ-100.
            CollectionAssert.AreEqual(Chain(777UL), Chain(777UL));
            CollectionAssert.AreNotEqual(Chain(777UL), Chain(778UL));
        }

        // ---- добыча ----

        [Test]
        public void ItemRewards_TravelIntoTheBag()
        {
            GameSession session = Session();
            Assert.AreEqual(0, session.Camp.Bag.Used, "сумка начинается пустой");

            int items = 0;
            session.Step(Command(CampCommand.EnterRift));

            // Несколько Разломов подряд: награда роллится случайно, и предмет
            // выпадает не в каждой тройке.
            for (int rift = 0; rift < 12 && items == 0; rift++)
            {
                ClearRift(session);
                session.Step(Idle);

                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    if (session.Run.GetOffer(i).Kind == RewardKind.Item)
                    {
                        var take = new InputFrame
                        { Command = (byte)((int)RunCommand.ChooseReward1 + i) };
                        session.Step(in take);
                        items++;
                        break;
                    }

                if (items == 0)
                {
                    var any = new InputFrame { Command = (byte)RunCommand.ChooseReward1 };
                    session.Step(in any);
                }
                SalvageIfReplacing(session);
            }

            Assert.Greater(items, 0, "за двенадцать Разломов предмет обязан предложиться хоть раз");

            var leave = new InputFrame { Command = (byte)RunCommand.Leave };
            session.Step(in leave);

            Assert.AreEqual(items, session.LastRun.ItemsKept);
            Assert.AreEqual(items, session.Camp.Bag.Used, "добытое доехало до сумки");
        }

        [Test]
        public void FullBag_LosesWhatDoesNotFit()
        {
            var camp = new Camp(PrototypeContent.Items(), act: 3, bagSlots: 1);
            camp.Bag.Add(Sword(1UL));

            var session = new GameSession(Seed, camp, PrototypeContent.Modules(),
                PrototypeContent.ItemBaseIds());

            session.Step(Command(CampCommand.EnterRift));

            // Идём по Разломам, пока в тройке не окажется предмет: вид награды
            // роллится, и ждать его в первой же тройке нечестно.
            bool tookItem = false;
            for (int rift = 0; rift < 12 && !tookItem; rift++)
            {
                ClearRift(session);
                session.Step(Idle);

                int choice = 0;
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    if (session.Run.GetOffer(i).Kind == RewardKind.Item)
                    {
                        choice = i;
                        tookItem = true;
                        break;
                    }

                var take = new InputFrame
                { Command = (byte)((int)RunCommand.ChooseReward1 + choice) };
                session.Step(in take);
                SalvageIfReplacing(session);
            }

            Assert.IsTrue(tookItem, "за двенадцать Разломов предмет обязан предложиться хоть раз");

            var leave = new InputFrame { Command = (byte)RunCommand.Leave };
            session.Step(in leave);

            Assert.AreEqual(0, session.LastRun.ItemsKept, "сумка была занята целиком");
            Assert.AreEqual(1, session.LastRun.ItemsLost,
                "не влезшее теряется — и это решение, принятое до входа");
        }

        // ---- снаряжение переживает забеги ----

        [Test]
        public void Equipment_PutOnInCamp_WorksInsideTheRift()
        {
            Camp camp = PrototypeContent.NewCamp();
            int slot = camp.Bag.Add(Sword(0x5EEDUL));
            Assert.IsTrue(camp.EquipFromBag(slot), "меч надет в лагере");
            Assert.AreEqual(0, camp.Bag.Used, "и ушёл из сумки");

            var session = new GameSession(Seed, camp, PrototypeContent.Modules(),
                PrototypeContent.ItemBaseIds());
            session.Step(Command(CampCommand.EnterRift));

            int armed = session.Run.Sim.Entities.Damage[Simulation.PlayerId];
            Assert.Greater(armed, 34, "надетое в лагере работает в Разломе");

            // И переживает вход в следующий забег: симуляция там новая.
            PlayOneRift(session);
            session.Step(Command(CampCommand.RepeatRift));

            Assert.AreEqual(armed, session.Run.Sim.Entities.Damage[Simulation.PlayerId],
                "снаряжение принадлежит персонажу, а не симуляции");
        }

        // ---- услуги по актам ----

        // ---- Полигон ----

        private static void StandOnGround(GameSession session, int ticks)
        {
            session.Step(Command(CampCommand.ToggleProvingGround));
            for (int t = 0; t < ticks; t++) session.Step(Attacking);
        }

        [Test]
        public void ProvingGround_CountsWhatTheBuildDeals()
        {
            GameSession session = Session();
            StandOnGround(session, 120);

            Assert.IsTrue(session.OnProvingGround);
            Assert.AreEqual(GameMode.Camp, session.Mode, "Полигон стоит в лагере, а не вместо него");
            Assert.Greater(session.Ground.DamageTotal, 0, "манекен обязан получать урон");
            Assert.Greater(session.Ground.Hits, 0);
            Assert.Greater(session.Ground.DamagePerSecond, 0);
        }

    }
}
