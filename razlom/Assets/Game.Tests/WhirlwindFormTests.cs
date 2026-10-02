using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Механика трёх форм Вихря (владелец 02.10, Simulation.WhirlwindForms): Буря,
    /// Водоворот, Пенные волны. Форма ставится узлами таблицы (PelagForms.AppendFormNodes),
    /// как её ставит набор забега. Числа — заглушки, тесты держат правила и порядок
    /// величин, а не баланс. Без формы Вихрь прежний бит в бит — пин снят с кода до
    /// механики форм (WhirlwindBaselineScenarios).
    /// </summary>
    public sealed class WhirlwindFormTests
    {
        private const int Slot = 0;
        private const int Health = 100000;

        // Вихрь без формы: без талантов с удержанием и ходьбой, вся линия так же, вся линия стоя.
        // Сняты 02.10 с Game.Sim до механики форм (рабочая копия без этой правки).
        private static readonly ulong[] NoFormFolds = { 0x63AF22E9786530E1UL, 0x23E44ABDAA1D640FUL, 0x8A41F83B5D407DADUL };

        private static Simulation Arena(PelagForm form, params AbilityNode[] extra)
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            Give(sim, form, extra);
            return sim;
        }

        private static void Give(Simulation sim, PelagForm form, params AbilityNode[] extra)
        {
            var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
            int count = PelagForms.AppendFormNodes(form, nodes, 0);
            foreach (AbilityNode node in extra) nodes[count++] = node;
            sim.SetAbility(Slot, AbilityDefinition.Whirlwind(), nodes, count);
        }

        /// <summary>Неподвижный враг без удара в (x, y) мм от начала координат (там герой).</summary>
        private static int Enemy(Simulation sim, int xMm, int yMm)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio(xMm, 1000), Fix64.Ratio(yMm, 1000)), Health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(1, 10);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static readonly FixVec2 FarAway = new FixVec2(Fix64.FromInt(100), Fix64.Zero);

        private static InputFrame Frame(bool press, bool hold, bool walk = false)
        {
            var input = InputFrame.Empty;
            input.AttackTarget = -1;
            input.AbilityTarget = -1;
            if (press) input.AbilityMask = 1 << Slot;
            if (hold) input.AbilityHoldMask = 1 << Slot;
            input.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            if (walk)
            {
                input.Flags = (byte)InputFlags.MoveOrder;
                input.Aim = FarAway;
            }
            return input;
        }

        /// <summary>События с тиком шага (номер шага от нуля).</summary>
        private static List<(int tick, SimEvent ev)> Run(Simulation sim, int ticks, System.Func<int, InputFrame> input)
        {
            var events = new List<(int, SimEvent)>();
            for (int t = 0; t < ticks; t++)
            {
                sim.Step(input(t));
                foreach (SimEvent ev in sim.Events) events.Add((t, ev));
            }
            return events;
        }

        private static List<(int tick, SimEvent ev)> Of(List<(int tick, SimEvent ev)> events, SimEventType type)
            => events.FindAll(e => e.ev.Type == type);

        private static int Lost(Simulation sim, int id) => Health - sim.Entities.Health[id];

        private static int SingleWhirlwind()
        {
            Simulation sim = Arena(PelagForm.None);
            int dummy = Enemy(sim, 1000, 0);
            Run(sim, 40, t => Frame(t == 0, false));
            return Lost(sim, dummy);
        }

        // ---- форма включена только узлом ----

        [Test]
        public void Table_ThreeWhirlwindFormsAreReady_OnTheMoveIsRetired_SwitchStaysOff()
        {
            Assert.IsFalse(FormRewardRules.UseSkillForms, "обычные забеги без форм до приёмки");
            Assert.IsTrue(PelagForms.IsReady(PelagForm.WhirlwindStorm));
            Assert.IsTrue(PelagForms.IsReady(PelagForm.WhirlwindMaelstrom));
            Assert.IsTrue(PelagForms.IsReady(PelagForm.WhirlwindFoamWaves));
            Assert.IsTrue(PelagForms.IsRetired(PelagForm.WhirlwindOnTheMove));
            Assert.IsFalse(PelagForms.IsValid(PelagForm.WhirlwindOnTheMove));
            Assert.IsFalse(PelagForms.IsReady(PelagForm.WhirlwindOnTheMove));
            Assert.AreEqual(3, PelagForms.ReadyFormCount(PelagKit.StarterPoolIndex));
        }

        [Test]
        public void StormNodes_CarryTheWalkShareAsAStat()
        {
            Simulation storm = Arena(PelagForm.WhirlwindStorm);
            Assert.AreEqual(PelagForms.StormMoveMultiplier, storm.GetAbility(Slot).Get(AbilityStatType.StartMoveMultiplier));
            Simulation plain = Arena(PelagForm.None);
            Assert.AreEqual(Fix64.Zero, plain.GetAbility(Slot).Get(AbilityStatType.StartMoveMultiplier));
        }

        [Test]
        public void NoForm_WhirlwindIsBitIdenticalToTheCodeBeforeForms()
            => CollectionAssert.AreEqual(NoFormFolds, WhirlwindBaselineScenarios.Folds());

        [Test]
        public void NoForm_HoldingDoesNothing_AndNoFormEvents()
        {
            Simulation sim = Arena(PelagForm.None);
            int dummy = Enemy(sim, 1000, 0);
            var events = Run(sim, 120, t => Frame(t == 0, true));
            Assert.AreEqual(SingleWhirlwind(), Lost(sim, dummy), "удержание без формы и таланта добавило оборотов");
            foreach (var e in events)
                Assert.Less((int)e.ev.Type, (int)SimEventType.WhirlwindStormStarted, "событие формы без формы: " + e.ev.Type);
        }

        // ---- Буря ----

        [Test]
        public void Storm_FullHold_ThreeSeconds_AboutThreeTimesTheWhirlwind()
        {
            Simulation sim = Arena(PelagForm.WhirlwindStorm);
            int dummy = Enemy(sim, 1000, 0);
            bool storming = false;
            var events = Run(sim, 130, t =>
            {
                if (t == 50) storming = sim.WhirlwindStorming;
                return Frame(t == 0, true);
            });

            Assert.IsTrue(storming, "на 50-м тике Буря не крутилась");
            Assert.IsFalse(sim.WhirlwindStorming);
            Assert.AreEqual(1, Of(events, SimEventType.WhirlwindStormStarted).Count);
            var started = Of(events, SimEventType.WhirlwindStormStarted)[0];
            Assert.AreEqual(Simulation.WhirlwindContactDelayTicks, started.tick, "Буря начинается в контакт");
            Assert.AreEqual(Simulation.StormHoldTicks - Simulation.WhirlwindContactDelayTicks, started.ev.Amount);

            var pulses = Of(events, SimEventType.WhirlwindStormPulse);
            Assert.AreEqual(7, pulses.Count, "обороты каждые StormPulseTicks с контакта до предела");
            for (int i = 0; i < pulses.Count; i++)
            {
                Assert.AreEqual(Simulation.WhirlwindContactDelayTicks + (i + 1) * Simulation.StormPulseTicks, pulses[i].tick);
                Assert.AreEqual(1, pulses[i].ev.Amount, "в круге один враг");
            }

            var ended = Of(events, SimEventType.WhirlwindStormEnded);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(Simulation.StormHoldTicks, ended[0].tick, "3 с от нажатия");
            Assert.AreEqual((int)WhirlwindStormEnd.TimeUp, ended[0].ev.ActionVariant);
            Assert.AreEqual(0, ended[0].ev.Amount);
            Assert.IsFalse(ended[0].ev.Flag);

            int single = SingleWhirlwind();
            int full = Lost(sim, dummy);
            Assert.AreEqual(single + 7 * (single * Simulation.StormPulseDamagePercent / 100), full);
            Assert.That(full, Is.InRange(single * 3, single * 35 / 10), "полная Буря — 3–3,5 удара Вихря");
        }

        [Test]
        public void Storm_ReleaseStopsTheSpinAndTheDamage()
        {
            Simulation sim = Arena(PelagForm.WhirlwindStorm);
            int dummy = Enemy(sim, 1000, 0);
            var events = Run(sim, 120, t => Frame(t == 0, t < 40));

            var ended = Of(events, SimEventType.WhirlwindStormEnded);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(40, ended[0].tick);
            Assert.AreEqual((int)WhirlwindStormEnd.Released, ended[0].ev.ActionVariant);
            Assert.AreEqual(Simulation.StormHoldTicks - 40, ended[0].ev.Amount, "сколько не докрутила");
            Assert.AreEqual(2, Of(events, SimEventType.WhirlwindStormPulse).Count);
            foreach (var e in Of(events, SimEventType.Damage))
                Assert.Less(e.tick, 40, "урон после отпускания");
            int single = SingleWhirlwind();
            Assert.AreEqual(single + 2 * (single * Simulation.StormPulseDamagePercent / 100), Lost(sim, dummy));
        }

        [Test]
        public void Storm_CostsConcentration_AndStopsWhenItRunsOut()
        {
            // Полная Буря: цена каста и расход в секунду сверх неё.
            Simulation full = Arena(PelagForm.WhirlwindStorm);
            Simulation tap = Arena(PelagForm.WhirlwindStorm);
            Run(full, 100, t => Frame(t == 0, true));
            Run(tap, 100, t => Frame(t == 0, false));
            float spentMore = tap.Entities.Lavidium[Simulation.PlayerId].ToFloat() - full.Entities.Lavidium[Simulation.PlayerId].ToFloat();
            float expected = Simulation.StormDrainPerSecond * (Simulation.StormHoldTicks - Simulation.WhirlwindContactDelayTicks)
                / (float)Simulation.TicksPerSecond;
            Assert.AreEqual(expected, spentMore, 0.6f, "расход удержания");

            // Хватает на каст и чуть-чуть удержания.
            Simulation poor = Arena(PelagForm.WhirlwindStorm);
            poor.Entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(40);
            var events = Run(poor, 120, t => Frame(t == 0, true));
            var ended = Of(events, SimEventType.WhirlwindStormEnded);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((int)WhirlwindStormEnd.OutOfConcentration, ended[0].ev.ActionVariant);
            Assert.IsTrue(ended[0].ev.Flag, "Flag — кончилась Концентрация");
            Assert.Greater(ended[0].ev.Amount, 0);
            Assert.Less(ended[0].tick, Simulation.StormHoldTicks);
        }

        [Test]
        public void Storm_WorksWithoutTheTalent_AndOutranksIt()
        {
            // Талант «удержание» (2 с, оборот раз в полсекунды) и Буря вместе — числа Бури.
            var talent = new AbilityNode[SabreTalents.TalentsPerLine];
            int count = SabreTalents.AppendNodes(SabreTalentLine.Whirlwind, SabreTalents.WhirlwindChannelIndex + 1, talent, 0);
            var extra = new AbilityNode[count];
            System.Array.Copy(talent, extra, count);
            Simulation both = Arena(PelagForm.WhirlwindStorm, extra);
            Assert.IsTrue(both.GetAbility(Slot).Has(AbilityFlag.WhirlwindChannel));
            var events = Run(both, 120, t => Frame(t == 0, true));
            Assert.AreEqual(7, Of(events, SimEventType.WhirlwindStormPulse).Count);
            Assert.AreEqual(Simulation.StormHoldTicks, Of(events, SimEventType.WhirlwindStormEnded)[0].tick);
        }

        [Test]
        public void Storm_WalksAtTheStatShare_AndATalentStatRaisesIt()
        {
            Fix64 Walked(PelagForm form, bool cast, params AbilityNode[] extra)
            {
                Simulation sim = Arena(form, extra);
                Fix64 from = Fix64.Zero;
                Run(sim, 81, t =>
                {
                    if (t == 30) from = sim.Entities.Position[Simulation.PlayerId].X;
                    return Frame(cast && t == 0, cast, walk: true);
                });
                return sim.Entities.Position[Simulation.PlayerId].X - from;
            }

            float free = Walked(PelagForm.None, false).ToFloat();
            float storm = Walked(PelagForm.WhirlwindStorm, true).ToFloat();
            float raised = Walked(PelagForm.WhirlwindStorm, true, AbilityNode.StatMod("test.storm.full-walk",
                AbilityStatType.StartMoveMultiplier, ModifierOp.Flat, Fix64.One - PelagForms.StormMoveMultiplier)).ToFloat();
            float plainWhirl = Walked(PelagForm.None, true).ToFloat();

            Assert.Greater(free, 1f);
            Assert.AreEqual(PelagForms.StormMoveMultiplier.ToFloat(), storm / free, 0.03f, "шаг в Буре");
            Assert.AreEqual(1f, raised / free, 0.03f, "стат StartMoveMultiplier = 1 — полный шаг");
            Assert.AreEqual(1f, plainWhirl / free, 0.03f, "Вихрь без формы шаг после контакта не режет");
        }

        // ---- Водоворот ----

        [Test]
        public void Maelstrom_PullsLightBodies_StaggersEveryoneIn4m_HeavyAndEliteStay()
        {
            Simulation sim = Arena(PelagForm.WhirlwindMaelstrom);
            int light = Enemy(sim, 3500, 0);
            int heavy = Enemy(sim, 0, 3500);
            sim.Entities.PushWeight[heavy] = Fix64.Zero;          // как Вендиго, Шипомёт, босс
            int elite = Enemy(sim, -3500, 0);
            sim.MarkElite(elite);
            int far = Enemy(sim, 0, -4500);                       // край тела 4,4 м
            FixVec2 heavyAt = sim.Entities.Position[heavy], eliteAt = sim.Entities.Position[elite],
                farAt = sim.Entities.Position[far];

            var events = Run(sim, 14, t => Frame(t == 0, false));

            var pull = Of(events, SimEventType.WhirlwindMaelstromPull);
            Assert.AreEqual(1, pull.Count);
            Assert.AreEqual(0, pull[0].tick, "тяга — в каст");
            Assert.AreEqual(1, pull[0].ev.Amount, "тянут только лёгкого");
            Assert.AreEqual(Simulation.WhirlwindContactDelayTicks - 1, pull[0].ev.ActionVariant, "тяга кончается к контакту");

            Assert.Less(sim.Entities.Position[light].Length.ToFloat(), 1.5f, "лёгкого не стянуло");
            Assert.AreEqual(heavyAt, sim.Entities.Position[heavy], "тяжёлого сдвинуло");
            Assert.AreEqual(eliteAt, sim.Entities.Position[elite], "элиту сдвинуло");
            Assert.AreEqual(farAt, sim.Entities.Position[far]);

            var stunned = new HashSet<int>();
            foreach (var e in Of(events, SimEventType.Stun))
            {
                Assert.AreEqual(Simulation.WhirlwindContactDelayTicks, e.tick, "оглушение — в контакт");
                Assert.AreEqual(Simulation.MaelstromStaggerTicks, e.ev.Amount);
                stunned.Add(e.ev.Target);
            }
            CollectionAssert.AreEquivalent(new[] { light, heavy, elite }, stunned);

            Assert.AreEqual(SingleWhirlwind(), Lost(sim, light), "урон — обычный Вихрь");
            Assert.AreEqual(0, Lost(sim, heavy), "тяжёлому только оглушение");
            Assert.AreEqual(0, Lost(sim, elite));
            Assert.AreEqual(0, Lost(sim, far));
        }

        [Test]
        public void Maelstrom_NeitherPullsNorStunsTheBoss()
        {
            var sim = new Simulation(1234, 128);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, distance: Fix64.FromInt(3));
            Give(sim, PelagForm.WhirlwindMaelstrom);
            const int boss = 1;
            Assert.AreEqual(EnemyKind.ForestThicketMaster, sim.Entities.Kind[boss]);

            var events = Run(sim, 14, t => Frame(t == 0, false));
            Assert.AreEqual(0, Of(events, SimEventType.WhirlwindMaelstromPull)[0].ev.Amount);
            foreach (var e in Of(events, SimEventType.Stun)) Assert.AreNotEqual(boss, e.ev.Target, "босса оглушило");
            Assert.AreNotEqual((byte)ForcedMotionKind.Dragged, sim.Entities.ForcedKind[boss]);
        }

        // ---- Пенные волны ----

        [Test]
        public void FoamWaves_TwoRings_EachHitsOnce_SecondReachesFiveMeters()
        {
            Simulation sim = Arena(PelagForm.WhirlwindFoamWaves);
            int near = Enemy(sim, 1000, 0);      // только оборот: кольца рождаются дальше, в 1,3 м
            int close = Enemy(sim, -1300, 1300); // 1,84 м: оборот и оба кольца
            int mid = Enemy(sim, 0, 3000);       // оба кольца
            int outer = Enemy(sim, -4500, 0);    // только второе
            int beyond = Enemy(sim, 0, -5500);   // край 5,4 — никто
            int elite = Enemy(sim, 2100, -2100); // 2,97 м: оба кольца, но не толкается
            sim.MarkElite(elite);
            FixVec2 eliteAt = sim.Entities.Position[elite];

            var events = Run(sim, 45, t => Frame(t == 0, false));

            var rings = Of(events, SimEventType.WhirlwindFoamRing);
            Assert.AreEqual(2, rings.Count);
            int contact = Simulation.WhirlwindContactDelayTicks;
            Assert.AreEqual(contact, rings[0].tick);
            Assert.AreEqual(0, rings[0].ev.Amount);
            Assert.AreEqual(Simulation.FoamRingFirstTravelTicks, rings[0].ev.ActionVariant);
            Assert.AreEqual(contact + Simulation.FoamRingSecondDelayTicks, rings[1].tick);
            Assert.AreEqual(1, rings[1].ev.Amount);
            Assert.AreEqual(Simulation.FoamRingSecondTravelTicks, rings[1].ev.ActionVariant);
            Assert.AreEqual(FixVec2.Zero, rings[0].ev.Position, "центр — где герой был в контакт");
            Assert.AreEqual(Fix64.FromInt(5), Simulation.FoamRingOuterRadius(1));

            var hits = new Dictionary<(int, int), int>();
            foreach (var e in Of(events, SimEventType.WhirlwindFoamRingHit))
            {
                var key = (e.ev.Target, e.ev.Amount);
                hits[key] = hits.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            int Hits(int id, int ring) => hits.TryGetValue((id, ring), out int n) ? n : 0;
            foreach (int id in new[] { close, mid, elite })
            {
                Assert.AreEqual(1, Hits(id, 0), "первое кольцо, тело " + id);
                Assert.AreEqual(1, Hits(id, 1), "второе кольцо, тело " + id);
            }
            Assert.AreEqual(0, Hits(near, 0) + Hits(near, 1), "кольцо задело тело внутри 1,3 м");
            Assert.AreEqual(0, Hits(outer, 0), "первое кольцо дошло до 4,5 м");
            Assert.AreEqual(1, Hits(outer, 1));
            Assert.AreEqual(0, Hits(beyond, 0) + Hits(beyond, 1), "кольцо дальше 5 м");

            int single = SingleWhirlwind();
            int ring = single * Simulation.FoamRingDamagePercent / 100;
            Assert.AreEqual(single, Lost(sim, near));
            Assert.AreEqual(single + 2 * ring, Lost(sim, close));
            Assert.AreEqual(2 * ring, Lost(sim, mid));
            Assert.AreEqual(ring, Lost(sim, outer));
            Assert.AreEqual(0, Lost(sim, beyond));

            Assert.Greater(sim.Entities.Position[mid].Length.ToFloat(), 3.4f, "лёгкого кольцо не оттолкнуло");
            Assert.AreEqual(eliteAt, sim.Entities.Position[elite], "элиту толкнуло");
            Assert.IsFalse(sim.TryGetFoamRing(0, out _, out _), "кольца не кончились");
        }

        // ---- детерминизм ----

        /// <summary>Толпа живых мобов, три формы подряд (смена сборки посреди боя, как мини-меню).</summary>
        private static ulong FormsScenario(out int formEvents)
        {
            var sim = new Simulation(4242, 128);
            sim.SetupKindTestArena(EnemyKind.ForestGuardian, 3, distance: Fix64.FromInt(3));
            FixVec2 hero = sim.Entities.Position[Simulation.PlayerId];
            EnemyKind[] kinds = { EnemyKind.ForestRootSwarm, EnemyKind.ForestRootSwarm, EnemyKind.ForestBud, EnemyKind.ForestSplitter };
            for (int k = 0; k < kinds.Length; k++)
            {
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio(k * 2 + 1, 8);
                sim.AddKindTestEnemy(kinds[k], hero + FixVec2.FromAngle(angle) * Fix64.Ratio(25 + k * 6, 10), 100);
            }
            Give(sim, PelagForm.WhirlwindStorm);

            formEvents = 0;
            ulong fold = 14695981039346656037UL;
            for (int t = 0; t < 450; t++)
            {
                if (t == 150) Give(sim, PelagForm.WhirlwindMaelstrom);
                if (t == 300) Give(sim, PelagForm.WhirlwindFoamWaves);
                bool press = t == 5 || t == 160 || t == 230 || t == 310 || t == 400;
                bool hold = t >= 5 && t < 110;
                var input = Frame(press, hold);
                if (t % 100 < 60)
                {
                    input.Flags = (byte)InputFlags.MoveOrder;
                    input.Aim = hero + FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(t, 300)) * Fix64.FromInt(3);
                }
                sim.Step(input);
                sim.Entities.Health[Simulation.PlayerId] = sim.Entities.MaxHealth[Simulation.PlayerId];
                fold = Mix(fold, sim.StateHash());
                foreach (SimEvent ev in sim.Events)
                {
                    if (ev.Type >= SimEventType.WhirlwindStormStarted && ev.Type <= SimEventType.WhirlwindFoamRingHit) formEvents++;
                    fold = Mix(fold, (ulong)ev.Type); fold = Mix(fold, (ulong)(uint)ev.Source);
                    fold = Mix(fold, (ulong)(uint)ev.Target); fold = Mix(fold, (ulong)(uint)ev.Amount);
                    fold = Mix(fold, ev.Flag ? 1UL : 0UL); fold = Mix(fold, (ulong)ev.Position.X.Raw);
                    fold = Mix(fold, (ulong)ev.Position.Y.Raw); fold = Mix(fold, (ulong)(uint)ev.ActionVariant);
                }
            }
            return fold;
        }

        [Test]
        public void ThreeForms_SameSeedSameInput_SameHashesAndEvents()
        {
            ulong a = FormsScenario(out int eventsA);
            ulong b = FormsScenario(out int eventsB);
            Assert.AreEqual(a, b);
            Assert.AreEqual(eventsA, eventsB);
            Assert.Greater(eventsA, 10, "сценарий не задел формы");
        }

        private static ulong Mix(ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++) { h ^= (v >> (i * 8)) & 0xFF; h *= 1099511628211UL; }
            return h;
        }
    }
}
