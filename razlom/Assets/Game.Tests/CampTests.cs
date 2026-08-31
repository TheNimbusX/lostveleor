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
        public void Session_StartsInCamp()
        {
            GameSession session = Session();

            Assert.AreEqual(GameMode.Camp, session.Mode);
            Assert.IsNull(session.Run, "в лагере забега нет");
            Assert.IsNull(session.ActiveSim, "и рисовать в лагере пока нечего");
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
            Assert.IsNull(session.ActiveSim, "в лагере не остаётся скрытая боевая симуляция");
            Assert.AreEqual(generationBefore + 1, session.Generation,
                "представление обязано пересобраться под лагерь");
        }

        [Test]
        public void WithoutThePortal_TheRiftIsUnreachable()
        {
            // Первый акт: портала ещё нет, он открывается третьим.
            var camp = new Camp(PrototypeContent.Items(), act: 1);
            var session = new GameSession(Seed, camp, PrototypeContent.Modules(),
                PrototypeContent.ItemBaseIds());

            session.Step(Command(CampCommand.EnterRift));

            Assert.AreEqual(GameMode.Camp, session.Mode,
                "закрытая услуга обязана просто не сработать, а не пустить в обход");
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
        public void Summary_HasRepeatInOneKey()
        {
            GameSession session = Session();
            session.Step(Command(CampCommand.EnterRift));
            PlayOneRift(session);

            session.Step(Command(CampCommand.RepeatRift));

            Assert.AreEqual(GameMode.Rift, session.Mode, "повтор идёт мимо лагеря");
            Assert.AreEqual(2, session.RunNumber);
        }

        [Test]
        public void Summary_AlsoLeadsBackToCamp()
        {
            GameSession session = Session();
            session.Step(Command(CampCommand.EnterRift));
            PlayOneRift(session);

            session.Step(Command(CampCommand.ReturnToCamp));

            Assert.AreEqual(GameMode.Camp, session.Mode);
            Assert.IsNull(session.Run);
        }

        [Test]
        public void EveryRun_GetsItsOwnSeed()
        {
            GameSession session = Session();

            session.Step(Command(CampCommand.EnterRift));
            ulong first = session.LastRunSeed;

            PlayOneRift(session);
            session.Step(Command(CampCommand.RepeatRift));

            Assert.AreNotEqual(first, session.LastRunSeed, "два забега подряд не могут быть одним");
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
            }

            Assert.IsTrue(tookItem, "за двенадцать Разломов предмет обязан предложиться хоть раз");

            var leave = new InputFrame { Command = (byte)RunCommand.Leave };
            session.Step(in leave);

            Assert.AreEqual(0, session.LastRun.ItemsKept, "сумка была занята целиком");
            Assert.AreEqual(1, session.LastRun.ItemsLost,
                "не влезшее теряется — и это решение, принятое до входа");
        }

        [Test]
        public void Salvage_TurnsUnkeptItemsIntoShards()
        {
            Camp camp = PrototypeContent.NewCamp();
            camp.Bag.Add(Sword(1UL));
            int keeper = camp.Bag.Add(Sword(2UL));
            camp.Bag.SetKeep(keeper, true);

            int shards = camp.SalvageJunk();

            Assert.Greater(shards, 0, "разбор обязан что-то дать");
            Assert.AreEqual(shards, camp.Money(CurrencyType.Shards));
            Assert.AreEqual(1, camp.Bag.Used, "помеченное «беречь» разбор не трогает");
            Assert.IsFalse(camp.Bag.IsEmpty(keeper));
        }

        [Test]
        public void Trader_PaysGold()
        {
            Camp camp = PrototypeContent.NewCamp();
            int slot = camp.Bag.Add(Sword(3UL));

            int gold = camp.SellToTrader(slot);

            Assert.Greater(gold, 0);
            Assert.AreEqual(gold, camp.Money(CurrencyType.Gold));
            Assert.AreEqual(0, camp.Bag.Used);
        }

        [Test]
        public void Wallet_DoesNotGoNegative()
        {
            Camp camp = PrototypeContent.NewCamp();
            camp.Earn(CurrencyType.Gold, 10);

            Assert.IsFalse(camp.Spend(CurrencyType.Gold, 11), "нечем — значит не потратил");
            Assert.AreEqual(10, camp.Money(CurrencyType.Gold), "и кошелёк не тронут");
            Assert.IsTrue(camp.Spend(CurrencyType.Gold, 10));
            Assert.AreEqual(0, camp.Money(CurrencyType.Gold));
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

        [Test]
        public void EquipFromBag_ReturnsTheReplacedItem()
        {
            Camp camp = PrototypeContent.NewCamp();
            int first = camp.Bag.Add(Sword(1UL));
            camp.EquipFromBag(first);

            int second = camp.Bag.Add(Sword(2UL));
            camp.EquipFromBag(second);

            Assert.AreEqual(2UL, camp.Worn.Worn(EquipSlot.Weapon).Seed, "надет второй");
            Assert.AreEqual(1, camp.Bag.Used, "первый вернулся в сумку, а не пропал");
        }

        [Test]
        public void UnequipToBag_RefusesWhenThereIsNoRoom()
        {
            var camp = new Camp(PrototypeContent.Items(), act: 3, bagSlots: 1);
            int slot = camp.Bag.Add(Jacket(1UL));
            camp.EquipFromBag(slot);
            camp.Bag.Add(Sword(2UL));

            Assert.IsFalse(camp.UnequipToBag(EquipSlot.Armor), "класть некуда");
            Assert.IsTrue(camp.Worn.IsWorn(EquipSlot.Armor), "значит вещь осталась надетой");
        }

        // ---- услуги по актам ----

        [Test]
        public void ActUnlocks_AreCumulative()
        {
            Camp camp = new Camp(PrototypeContent.Items(), act: 1);
            Assert.IsTrue(camp.Has(CampService.Smith));
            Assert.IsTrue(camp.Has(CampService.Trader));
            Assert.IsFalse(camp.Has(CampService.Chronicler));
            Assert.IsFalse(camp.Has(CampService.RiftPortal));

            camp.AdvanceToAct(3);

            Assert.IsTrue(camp.Has(CampService.Smith), "открытое не закрывается обратно");
            Assert.IsTrue(camp.Has(CampService.Chronicler), "второй акт тоже подтянулся");
            Assert.IsTrue(camp.Has(CampService.RiftPortal));
        }

        [Test]
        public void ActNeverGoesBackwards()
        {
            Camp camp = new Camp(PrototypeContent.Items(), act: 3);
            camp.AdvanceToAct(1);

            Assert.AreEqual(3, camp.Act);
            Assert.IsTrue(camp.Has(CampService.RiftPortal), "отобранный глагол — это откат прогресса");
        }

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

        [Test]
        public void ProvingGround_DummyDoesNotDie()
        {
            GameSession session = Session();
            session.EnterProvingGround(dummyHealth: 1);

            for (int t = 0; t < 200; t++) session.Step(Attacking);

            Assert.IsTrue(session.Ground.Sim.Entities.Alive[ProvingGround.DummyId],
                "мишень обязана пережить любой билд — иначе замер обрывается на сильном");
            Assert.Greater(session.Ground.Hits, 1, "и продолжать получать удары");
        }

        [Test]
        public void ProvingGround_ShowsTheDifferenceBetweenTwoSwords()
        {
            // Это и есть смысл Полигона: без него игрок не знает, какая
            // из двух найденных вещей лучше на его билде.
            long DamageWith(ItemInstance? sword)
            {
                Camp camp = PrototypeContent.NewCamp();
                if (sword.HasValue)
                {
                    int slot = camp.Bag.Add(sword.Value);
                    camp.EquipFromBag(slot);
                }

                var session = new GameSession(Seed, camp, PrototypeContent.Modules(),
                    PrototypeContent.ItemBaseIds());
                StandOnGround(session, 240);
                return session.Ground.DamageTotal;
            }

            long bare = DamageWith(null);
            long armed = DamageWith(Sword(0xA11CEUL));

            Assert.Greater(bare, 0);
            Assert.Greater(armed, bare, "надетый меч обязан быть виден на счётчике");
        }

        [Test]
        public void ProvingGround_SeparatesFireFromPhysical()
        {
            GameSession session = Session();
            session.Step(Command(CampCommand.ToggleProvingGround));

            session.Ground.Sim.SetAbility(0, AbilityDefinition.FlameSeal(),
                new AbilityNode[0], 0);

            var cast = new InputFrame
            {
                Aim = new FixVec2(Fix64.Ratio(3, 2), Fix64.Zero),
                AbilityMask = 1,
                Flags = (byte)InputFlags.Attack,
            };
            session.Step(in cast);
            for (int t = 0; t < 120; t++) session.Step(Attacking);

            Assert.Greater(session.Ground.FireDamage, 0, "«Печать пламени» бьёт огнём");
            Assert.Greater(session.Ground.PhysicalDamage, 0, "а автоатака — физически");
            Assert.AreEqual(session.Ground.DamageTotal,
                session.Ground.FireDamage + session.Ground.PhysicalDamage,
                "разбивка обязана сходиться с итогом");
        }

        [Test]
        public void ProvingGround_ResistanceOnTheDummyIsVisible()
        {
            long FireDamageAgainst(Fix64 resist)
            {
                GameSession session = Session();
                session.Step(Command(CampCommand.ToggleProvingGround));
                session.RetuneDummy(100000, Fix64.Zero, resist);

                session.Ground.Sim.SetAbility(0, AbilityDefinition.FlameSeal(),
                    new AbilityNode[0], 0);

                var cast = new InputFrame
                {
                    Aim = new FixVec2(Fix64.Ratio(3, 2), Fix64.Zero),
                    AbilityMask = 1,
                    Flags = (byte)InputFlags.Attack,
                };
                session.Step(in cast);
                for (int t = 0; t < 120; t++) session.Step(Attacking);

                return session.Ground.FireDamage;
            }

            long soft = FireDamageAgainst(Fix64.Zero);
            long tough = FireDamageAgainst(Fix64.Ratio(75, 100));

            Assert.Greater(soft, 0);
            Assert.Less(tough, soft, "настраиваемое сопротивление на то и настраиваемое");
        }

        [Test]
        public void EnteringTheRift_StepsOffTheProvingGround()
        {
            GameSession session = Session();
            session.Step(Command(CampCommand.ToggleProvingGround));
            Assert.IsTrue(session.OnProvingGround);

            session.Step(Command(CampCommand.EnterRift));

            Assert.IsFalse(session.OnProvingGround, "Полигон — часть лагеря, а не походный инвентарь");
            Assert.AreSame(session.Run.Sim, session.ActiveSim);
        }

        [Test]
        public void ActiveSimulation_ChangesGeneration()
        {
            GameSession session = Session();
            int start = session.Generation;

            session.Step(Command(CampCommand.ToggleProvingGround));
            Assert.Greater(session.Generation, start,
                "представление обязано узнать, что индексы сущностей начались заново");

            int onGround = session.Generation;
            session.Step(Command(CampCommand.EnterRift));
            Assert.Greater(session.Generation, onGround);
        }
    }
}
