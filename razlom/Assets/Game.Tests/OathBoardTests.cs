using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Доска клятв в лагере (решение 06.10, план «Лагерь 06–10.10», T2).
    ///
    /// Цена — общая лестница 60 + 20 × уже куплено (ступень — тоже покупка), у клятв героя три
    /// ступени, у остальных одна. Включено не больше 3/4/5/6 на ур. 1/6/12/18. Статовые клятвы
    /// работают и в лагере, тестовый забег идёт без клятв. Сохранение — секция 9.
    /// </summary>
    public sealed class OathBoardTests
    {
        private static InputFrame Command(RunCommand command) => new InputFrame { Command = (byte)command };

        private static Camp RichCamp(int ash = 100000)
        {
            Camp camp = PrototypeContent.NewCamp();
            camp.Earn(CurrencyType.Ash, ash);
            return camp;
        }

        private static GameSession Session(Camp camp, bool boss = false, ulong seed = 5)
        {
            LocationDefinition location = FormBaselineScenarios.Location(boss);
            return new GameSession(seed, camp, location.Modules, PrototypeContent.ItemBaseIds(), location: location);
        }

        /// <summary>Снимает врагов, ставит героя у выхода, берёт первую карточку и первый путь.</summary>
        private static void ClearArena(GameSession session)
        {
            RiftRun run = session.Run;
            for (int guard = 0; guard < 4000 && run.Phase == RunPhase.Clearing; guard++)
            {
                EntityStore e = run.Sim.Entities;
                for (int i = 0; i < e.Count; i++)
                    if (e.Side[i] != Faction.Wole) e.Alive[i] = false;
                session.Step(InputFrame.Empty);
            }
            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            session.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward, run.Phase);
            session.Step(FormBaselineScenarios.Choice(0));
            if (run.Phase == RunPhase.ReplacingAbility) session.Step(Command(RunCommand.ReplaceSlot2));
            Assert.AreEqual(RunPhase.ChoosingRoute, run.Phase);
            session.Step(Command(RunCommand.ChooseRoute1));
        }

        [Test]
        public void PricesRiseByTwentyPerPurchaseIncludingTiers()
        {
            Camp camp = RichCamp(60 + 80 + 100);
            Assert.AreEqual(60, camp.NextOathPrice);
            Assert.AreEqual(OathResult.Success, camp.BuyOath(OathId.ToughHide));
            Assert.AreEqual(80, camp.NextOathPrice);
            Assert.AreEqual(OathResult.Success, camp.BuyOath(OathId.ToughHide), "вторая ступень — тоже покупка");
            Assert.AreEqual(100, camp.NextOathPrice);
            Assert.AreEqual(OathResult.Success, camp.BuyOath(OathId.LastBreath));
            Assert.AreEqual(0, camp.Money(CurrencyType.Ash), "60 + 80 + 100");
            Assert.AreEqual(3, camp.OathPurchases);
            Assert.AreEqual(120, camp.NextOathPrice);

            camp.Earn(CurrencyType.Ash, 119);
            Assert.AreEqual(OathResult.NotEnoughAsh, camp.BuyOath(OathId.KeenEye));
            Assert.AreEqual(119, camp.Money(CurrencyType.Ash), "без пепла кошелёк не тронут");
            Assert.AreEqual(0, camp.OathRank(OathId.KeenEye));
        }

        [Test]
        public void HeroOathsCapAtThreeOthersAtOne()
        {
            Camp camp = RichCamp();
            camp.DeveloperSetLevel(18);
            for (int id = 1; id <= OathIds.Count; id++)
            {
                var oath = (OathId)id;
                int cap = id <= (int)OathId.QuickRoll ? 3 : 1;
                Assert.AreEqual(cap, RunBoons.MaxRank(oath), oath.ToString());
                for (int rank = 0; rank < cap; rank++) Assert.AreEqual(OathResult.Success, camp.BuyOath(oath));
                int ash = camp.Money(CurrencyType.Ash);
                Assert.AreEqual(OathResult.MaxRank, camp.BuyOath(oath), oath.ToString());
                Assert.AreEqual(ash, camp.Money(CurrencyType.Ash));
                Assert.AreEqual(cap, camp.OathRank(oath));
            }
            Assert.AreEqual(OathResult.InvalidOath, camp.BuyOath(OathId.None));
            Assert.AreEqual(OathResult.InvalidOath, camp.BuyOath((OathId)(OathIds.Count + 1)));
        }

        [Test]
        public void SlotsAreThreeFourFiveSixAtLevelsOneSixTwelveEighteen()
        {
            Camp camp = RichCamp();
            int[] levels = { 1, 5, 6, 11, 12, 17, 18, 30 }, slots = { 3, 3, 4, 4, 5, 5, 6, 6 };
            for (int i = 0; i < levels.Length; i++)
            {
                camp.DeveloperSetLevel(levels[i]);
                Assert.AreEqual(slots[i], camp.OathSlots, "ур. " + levels[i]);
            }
            Assert.AreEqual(6, new Camp(PrototypeContent.Items(), act: 3).OathSlots, "Sandbox — сразу 6");

            camp.DeveloperSetLevel(1);
            OathId[] bought = { OathId.ToughHide, OathId.HeavyHand, OathId.Steadfast, OathId.RingingCoin };
            foreach (OathId id in bought) Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
            Assert.IsTrue(camp.OathActive(OathId.ToughHide) && camp.OathActive(OathId.HeavyHand) && camp.OathActive(OathId.Steadfast),
                "новая клятва сама встаёт в свободный слот");
            Assert.IsFalse(camp.OathActive(OathId.RingingCoin), "четвёртой слота нет");
            Assert.AreEqual(3, camp.ActiveOathCount);
            Assert.AreEqual(OathResult.NoFreeSlot, camp.SetOathActive(OathId.RingingCoin, true));
            Assert.AreEqual(OathResult.NotOwned, camp.SetOathActive(OathId.Favor, true));
            Assert.AreEqual(OathResult.Success, camp.SetOathActive(OathId.HeavyHand, false));
            Assert.AreEqual(OathResult.Success, camp.SetOathActive(OathId.RingingCoin, true));
            Assert.IsTrue(camp.OathActive(OathId.RingingCoin));
            Assert.AreEqual(1, camp.OathRank(OathId.HeavyHand), "выключенная клятва остаётся купленной");

            RunBoons boons = camp.CreateRunBoons();
            Assert.AreEqual(1, boons.Rank(OathId.ToughHide));
            Assert.AreEqual(0, boons.Rank(OathId.HeavyHand), "в забег — только включённые");
            Assert.AreEqual(1, boons.Rank(OathId.RingingCoin));
        }

        [Test]
        public void StatOathsReachRunAndCampHero()
        {
            Camp plainCamp = PrototypeContent.NewCamp(), camp = RichCamp();
            GameSession plain = Session(plainCamp), session = Session(camp);
            Assert.AreEqual(OathResult.Success, session.BuyOath(OathId.ToughHide));
            Assert.AreEqual(OathResult.Success, session.BuyOath(OathId.HeavyHand));
            Assert.AreEqual(OathResult.Success, session.BuyOath(OathId.LightStep));

            // Лагерь: манекены чувствуют шкуру и шаг, но не руку — она только в забеге.
            EntityStore campHero = session.CampSim.Entities, plainHero = plain.CampSim.Entities;
            Assert.AreEqual(plainHero.MaxHealth[0] + 30, campHero.MaxHealth[0]);
            Assert.Greater(campHero.MoveStep[0].Raw, plainHero.MoveStep[0].Raw);
            Assert.AreEqual(0, session.CampSim.Boons.Rank(OathId.HeavyHand));

            plain.EnterRift();
            session.EnterRift();
            Assert.AreEqual(1, session.Run.Boons.Rank(OathId.HeavyHand), "в забег — весь снимок");
            plain.Step(InputFrame.Empty);
            session.Step(InputFrame.Empty);
            EntityStore run = session.Run.Sim.Entities, plainRun = plain.Run.Sim.Entities;
            Assert.AreEqual(plainRun.MaxHealth[0] + 30, run.MaxHealth[0]);
            Assert.AreEqual(run.MaxHealth[0], run.Health[0]);
            Assert.Greater(run.MoveStep[0].Raw, plainRun.MoveStep[0].Raw);
        }

        [Test]
        public void DeveloperRunIgnoresOaths()
        {
            LocationDefinition location = FormBaselineScenarios.Location(false);
            Camp camp = RichCamp();
            GameSession session = Session(camp), plain = Session(PrototypeContent.NewCamp());
            Assert.AreEqual(OathResult.Success, session.BuyOath(OathId.ToughHide));
            Assert.AreEqual(OathResult.Success, session.BuyOath(OathId.TenaciousHands));
            session.StartDeveloperRift(location, 1, false, 5);
            plain.StartDeveloperRift(location, 1, false, 5);
            session.Step(InputFrame.Empty);
            plain.Step(InputFrame.Empty);
            Assert.IsTrue(session.Run.Boons.IsEmpty, "тестовый забег — эталонный герой без клятв (M6)");
            Assert.IsTrue(session.Run.Sim.Boons.IsEmpty);
            Assert.AreEqual(plain.Run.Sim.Entities.MaxHealth[0], session.Run.Sim.Entities.MaxHealth[0]);
            Assert.AreEqual(plain.Run.Sim.StateHash(), session.Run.Sim.StateHash());
        }

        [Test]
        public void AshTrailRingingCoinGripHandsRuneSageApply()
        {
            // Проценты снимка: монета 115, пепел 120, руки 75 при смерти (вместо 50).
            RunBoons none = RunBoons.Empty;
            Assert.AreEqual(100, none.GoldPercent);
            Assert.AreEqual(100, none.AshPercent);
            Assert.AreEqual(RunEconomy.DeathGoldKeptPercent, none.DeathGoldPercent);
            Assert.AreEqual(115, none.WithRank(OathId.RingingCoin, 1).GoldPercent);
            Assert.AreEqual(120, none.WithRank(OathId.AshTrail, 1).AshPercent);
            Assert.AreEqual(75, none.WithRank(OathId.TenaciousHands, 1).DeathGoldPercent);

            // «Звонкая монета» — от суммы найденного: А1–А4 по 2×N = 20 → 23.
            Camp coinCamp = RichCamp();
            Assert.AreEqual(OathResult.Success, coinCamp.BuyOath(OathId.RingingCoin));
            GameSession plain = Session(PrototypeContent.NewCamp(), seed: 9), coin = Session(coinCamp, seed: 9);
            plain.EnterRift();
            coin.EnterRift();
            for (int arena = 0; arena < 4; arena++) { ClearArena(plain); ClearArena(coin); }
            int found = plain.Run.Gold;
            Assert.Greater(found, 0);
            Assert.AreEqual(RunEconomy.Percent(found, 115), coin.Run.Gold);
            Assert.Greater(coin.Run.Gold, found);

            // «Знаток рун» — процент для разбора у Эни, только пока клятва включена.
            Camp sage = RichCamp();
            Assert.AreEqual(0, sage.RuneSageBonusPercent);
            Assert.AreEqual(OathResult.Success, sage.BuyOath(OathId.RuneSage));
            Assert.AreEqual(25, sage.RuneSageBonusPercent);
            Assert.AreEqual(OathResult.Success, sage.SetOathActive(OathId.RuneSage, false));
            Assert.AreEqual(0, sage.RuneSageBonusPercent);
        }

        [Test]
        public void ExcessActiveOathsTrimmedOnLoad()
        {
            Camp camp = RichCamp();
            camp.DeveloperSetLevel(18);
            OathId[] bought = { OathId.RuneSage, OathId.ToughHide, OathId.Steadfast, OathId.HeavyHand, OathId.LastBreath, OathId.LightStep };
            foreach (OathId id in bought) Assert.AreEqual(OathResult.Success, camp.BuyOath(id));
            Assert.AreEqual(OathResult.Success, camp.BuyOath(OathId.ToughHide));
            Assert.AreEqual(6, camp.ActiveOathCount);

            // Круг без правок: побайтово тот же файл, те же ступени и включённость.
            byte[] bytes = CampSaveCodec.Encode(camp);
            Camp same = CampSaveCodec.Decode(bytes, camp.Items);
            CollectionAssert.AreEqual(bytes, CampSaveCodec.Encode(same));
            Assert.AreEqual(2, same.OathRank(OathId.ToughHide));
            Assert.AreEqual(6, same.ActiveOathCount);
            Assert.AreEqual(camp.NextOathPrice, same.NextOathPrice);

            // Уровень в файле — 1 (как после смены кривой): слотов 3, включёнными остаются младшие номера.
            byte[] low = WithLevel(bytes, 1);
            Camp trimmed = CampSaveCodec.Decode(low, camp.Items);
            Assert.AreEqual(3, trimmed.OathSlots);
            Assert.AreEqual(3, trimmed.ActiveOathCount);
            Assert.IsTrue(trimmed.OathActive(OathId.ToughHide) && trimmed.OathActive(OathId.HeavyHand) && trimmed.OathActive(OathId.LightStep));
            Assert.IsFalse(trimmed.OathActive(OathId.LastBreath) || trimmed.OathActive(OathId.Steadfast) || trimmed.OathActive(OathId.RuneSage));
            foreach (OathId id in bought) Assert.Greater(trimmed.OathRank(id), 0, "купленное не пропадает");

            // Без клятв секции нет вовсе: файлы без доски те же, что и до неё.
            Camp empty = PrototypeContent.NewCamp();
            byte[] plain = CampSaveCodec.Encode(empty);
            Assert.AreEqual(0, CampSaveCodec.Decode(plain, empty.Items).OathPurchases);
            Assert.IsFalse(HasSection(plain, 9));
            Assert.IsTrue(HasSection(bytes, 9));
        }

        /// <summary>
        /// Стыки с общими файлами: переключение клятвы видно в хеше лагеря (по нему CampSaveStore
        /// пишет файл), а герой лагеря после загрузки сохранения сразу несёт статовые клятвы.
        /// </summary>
        [Test]
        public void ToggleReachesCampHashAndLoadedCampHeroWearsOaths()
        {
            Camp camp = RichCamp();
            Assert.AreEqual(OathResult.Success, camp.BuyOath(OathId.ToughHide));
            ulong before = 0, after = 0;
            camp.HashInto(ref before);
            Assert.AreEqual(OathResult.Success, camp.SetOathActive(OathId.ToughHide, false));
            camp.HashInto(ref after);
            Assert.AreNotEqual(before, after, "выключение без смены кошелька должно сохраниться");
            Assert.AreEqual(OathResult.Success, camp.SetOathActive(OathId.ToughHide, true));

            Camp loaded = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
            GameSession session = Session(loaded), plain = Session(PrototypeContent.NewCamp());
            Assert.AreEqual(plain.CampSim.Entities.MaxHealth[0] + Simulation.ToughHideHealthPerRank,
                session.CampSim.Entities.MaxHealth[0]);
        }

        // Ядро — первая секция: заголовок файла 8 байт, заголовок секции 6, затем флаги (1), акт (1),
        // вместимость сумки (4) — уровень лежит с 20-го байта (CampSaveCodec.Encode).
        private static byte[] WithLevel(byte[] bytes, int level)
        {
            var copy = (byte[])bytes.Clone();
            Assert.AreEqual(1, BitConverter.ToUInt16(copy, 8), "первая секция — ядро");
            Array.Copy(BitConverter.GetBytes(level), 0, copy, 20, 4);
            Array.Copy(BitConverter.GetBytes(Fnv(copy, copy.Length - 4)), 0, copy, copy.Length - 4, 4);
            return copy;
        }

        private static bool HasSection(byte[] bytes, ushort tag)
        {
            for (int at = 8; at + 6 <= bytes.Length - 4; at += 6 + BitConverter.ToInt32(bytes, at + 2))
                if (BitConverter.ToUInt16(bytes, at) == tag) return true;
            return false;
        }

        private static uint Fnv(byte[] bytes, int count)
        {
            uint h = 2166136261;
            for (int i = 0; i < count; i++) { h ^= bytes[i]; h = unchecked(h * 16777619); }
            return h;
        }
    }
}
