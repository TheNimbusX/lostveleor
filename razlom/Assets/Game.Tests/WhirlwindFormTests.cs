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

            var events = Run(sim, Simulation.MaelstromContactDelayTicks + 4, t => Frame(t == 0, false));

            var pull = Of(events, SimEventType.WhirlwindMaelstromPull);
            Assert.AreEqual(1, pull.Count);
            Assert.AreEqual(0, pull[0].tick, "тяга — в каст");
            Assert.AreEqual(1, pull[0].ev.Amount, "тянут только лёгкого");
            Assert.AreEqual(Simulation.MaelstromPullTicks, pull[0].ev.ActionVariant, "тяга — 16 тиков");
            Assert.AreEqual(Simulation.MaelstromContactDelayTicks - 1, pull[0].ev.ActionVariant, "тяга кончается к контакту");

            Assert.Less(sim.Entities.Position[light].Length.ToFloat(), 1.5f, "лёгкого не стянуло");
            Assert.AreEqual(heavyAt, sim.Entities.Position[heavy], "тяжёлого сдвинуло");
            Assert.AreEqual(eliteAt, sim.Entities.Position[elite], "элиту сдвинуло");
            Assert.AreEqual(farAt, sim.Entities.Position[far]);

            var stunned = new HashSet<int>();
            foreach (var e in Of(events, SimEventType.Stun))
            {
                Assert.AreEqual(Simulation.MaelstromContactDelayTicks, e.tick, "оглушение — в контакт, после тяги");
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

            var events = Run(sim, Simulation.MaelstromContactDelayTicks + 4, t => Frame(t == 0, false));
            Assert.AreEqual(0, Of(events, SimEventType.WhirlwindMaelstromPull)[0].ev.Amount);
            foreach (var e in Of(events, SimEventType.Stun)) Assert.AreNotEqual(boss, e.ev.Target, "босса оглушило");
            Assert.AreNotEqual((byte)ForcedMotionKind.Dragged, sim.Entities.ForcedKind[boss]);
        }

        [Test]
        public void Maelstrom_Pull16Ticks_EasesIn_EndsAtTheStop_ContactRightAfter()
        {
            const int pullTicks = Simulation.MaelstromPullTicks, contact = Simulation.MaelstromContactDelayTicks;
            Assert.AreEqual(16, pullTicks, "владелец 02.10: тяга ~0,5–0,6 с");
            Assert.AreEqual(pullTicks + 1, contact);

            Simulation sim = Arena(PelagForm.WhirlwindMaelstrom);
            int farBody = Enemy(sim, 3500, 0);
            int nearBody = Enemy(sim, 0, -2000);
            int[] bodies = { farBody, nearBody };
            Fix64 near = sim.Entities.BodyRadius[Simulation.PlayerId] + Fix64.Ratio(1, 2);
            var start = new FixVec2[2];
            var stop = new FixVec2[2];
            for (int k = 0; k < 2; k++)
            {
                start[k] = sim.Entities.Position[bodies[k]];
                Fix64 distance = start[k].Length;
                stop[k] = start[k] / distance * (near + sim.Entities.BodyRadius[bodies[k]]);
            }

            var at = new FixVec2[2, contact + 3];
            PlayerActionState action = default;
            var events = Run(sim, contact + 3, t =>
            {
                if (t > 0) for (int k = 0; k < 2; k++) at[k, t - 1] = sim.Entities.Position[bodies[k]];
                if (t == 1) action = sim.PlayerAction;
                return Frame(t == 0, false);
            });

            // Часы героя покрывают длинный каст так же, как прежние 10 тиков: контакт — после тяги.
            Assert.AreEqual(0, action.StartTick);
            Assert.AreEqual(contact, action.ContactTick, "лок героя до контакта Водоворота");
            Assert.AreEqual(contact + sim.AbilityExecutionTicks(12, 1), action.EndTick);

            for (int k = 0; k < 2; k++)
            {
                float total = (start[k] - stop[k]).Length.ToFloat();
                Assert.AreEqual(start[k], at[k, 0], "в тик каста тело ещё стоит, тело " + k);
                float previousStep = 0f;
                for (int t = 1; t <= pullTicks; t++)
                {
                    float travelled = (at[k, t] - start[k]).Length.ToFloat();
                    float expected = total * Simulation.MaelstromPullProgress(t, pullTicks).ToFloat();
                    Assert.AreEqual(expected, travelled, 2e-3f, $"доля пути по разгону, тело {k}, тик {t}");
                    float step = (at[k, t] - at[k, t - 1]).Length.ToFloat();
                    Assert.Greater(step, previousStep, $"тело разгоняется, тело {k}, тик {t}");
                    Assert.Less(at[k, t].Length.ToFloat(), at[k, t - 1].Length.ToFloat(), $"тело всё время едет к герою, тело {k}, тик {t}");
                    previousStep = step;
                }
                float linear = total / pullTicks;
                // Проверка 02.10: x² вёз 10 см из 160 за шесть тиков — «стоит, потом влетает».
                float first = (at[k, 1] - at[k, 0]).Length.ToFloat();
                Assert.GreaterOrEqual(first, linear * .2f, "трогается с первого тика, тело " + k);
                Assert.Less(first, linear * .5f, "но с места, а не ровным волоком, тело " + k);
                // Проверка 03.10: 30% к середине (изгиб 4) начинались незаметно — теперь треть.
                float half = (at[k, pullTicks / 2] - start[k]).Length.ToFloat() / total;
                Assert.GreaterOrEqual(half, .32f, "к середине тяги — треть пути, тело " + k);
                Assert.LessOrEqual(half, .35f, "к середине тяги — треть пути, тело " + k);
                Assert.Greater((at[k, pullTicks] - at[k, pullTicks - 1]).Length.ToFloat(), linear * 1.5f, "к концу влетает");
                Assert.AreNotEqual(at[k, pullTicks - 1], at[k, pullTicks], "тяга идёт до 16-го тика");
                Assert.AreEqual(stop[k].X.ToFloat(), at[k, pullTicks].X.ToFloat(), 1e-4f, "конец тяги — прежняя точка, тело " + k);
                Assert.AreEqual(stop[k].Y.ToFloat(), at[k, pullTicks].Y.ToFloat(), 1e-4f, "конец тяги — прежняя точка, тело " + k);
                Assert.AreEqual(at[k, pullTicks], at[k, contact], "к контакту тело стоит");
                Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[bodies[k]]);
            }

            // Контакт — сразу за концом тяги: урон и оглушение тиком позже последнего шага, не раньше.
            foreach (int body in bodies)
            {
                var hits = Of(events, SimEventType.Damage).FindAll(e => e.ev.Target == body);
                Assert.IsNotEmpty(hits, "Водоворот не ударил тело " + body);
                Assert.AreEqual(contact, hits[0].tick, "удар — в контакт");
            }
            foreach (var e in Of(events, SimEventType.Stun))
                Assert.AreEqual(contact, e.tick);
            Assert.AreEqual(2, Of(events, SimEventType.Stun).Count);
            Assert.AreEqual(SingleWhirlwind(), Lost(sim, farBody), "урон прежний — обычный Вихрь");
            Assert.AreEqual(SingleWhirlwind(), Lost(sim, nearBody));
        }

        [Test]
        public void Maelstrom_PullCurve_MovesFromTheFirstTick_AThirdByTheMiddle()
        {
            const int pullTicks = Simulation.MaelstromPullTicks;
            // Путь 1,6 м по тикам тяги, см: шаг 3,75 и растёт на 0,83 см за тик; к середине — 53,3 (треть).
            // Проверка 03.10: при изгибе 4 (первый шаг 2,5 см, к середине 30%) начало тяги не читалось.
            float[] cm = { 0f, 3.75f, 8.33333f, 13.75f, 20f, 27.08333f, 35f, 43.75f, 53.33333f, 63.75f, 75f,
                87.08333f, 100f, 113.75f, 128.33333f, 143.75f, 160f };
            Assert.AreEqual(pullTicks + 1, cm.Length);
            for (int t = 0; t <= pullTicks; t++)
                Assert.AreEqual(cm[t], 160f * Simulation.MaelstromPullProgress(t, pullTicks).ToFloat(), 1e-3f, "тик " + t);
        }

        /// <summary>Комната 20×20 с центром в нуле и камнем (как EnemyBrainTests.Room); герой в нуле.</summary>
        private static Simulation RockArena(double rockX, double rockY, double rockRadius)
        {
            var room = new ModuleDefinition("maelstrom.rock", 10, 10, new ModuleConnector[0], isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }));
            map.TryPlace(0, 0, -5, -5);
            map.AddTestObstacle(new LayoutObstacle(new FixVec2(Fix64.FromDouble(rockX), Fix64.FromDouble(rockY)),
                Fix64.FromDouble(rockRadius), 0));
            map.BuildRoutes();
            var sim = new Simulation(1234, 128);
            sim.SetupRift(map, 1234UL, 0, 0, 100);
            sim.PlayerInvulnerable = true;
            sim.Entities.Position[Simulation.PlayerId] = FixVec2.Zero;
            Give(sim, PelagForm.WhirlwindMaelstrom);
            return sim;
        }

        [Test]
        public void Maelstrom_BodyBlockedByARock_WaitsThenCatchesUpGradually_NoJerk()
        {
            const int pullTicks = Simulation.MaelstromPullTicks, contact = Simulation.MaelstromContactDelayTicks;
            float catchUp = Simulation.MaelstromPullCatchUp.ToFloat();
            // Камень на пути: тело упирается и ползёт по его краю, потом сходит с него.
            Simulation sim = RockArena(2.4, -1.3, 0.3);
            int blocked = Enemy(sim, 3000, -1200);
            int free = Enemy(sim, 0, 3000);
            int[] bodies = { blocked, free };
            Fix64 near = sim.Entities.BodyRadius[Simulation.PlayerId] + Fix64.Ratio(1, 2);
            var total = new float[2];
            var stopAt = new float[2];
            for (int k = 0; k < 2; k++)
            {
                stopAt[k] = (near + sim.Entities.BodyRadius[bodies[k]]).ToFloat();
                total[k] = sim.Entities.Position[bodies[k]].Length.ToFloat() - stopAt[k];
            }

            var at = new FixVec2[2, contact + 3];
            Run(sim, contact + 3, t =>
            {
                if (t > 0) for (int k = 0; k < 2; k++) at[k, t - 1] = sim.Entities.Position[bodies[k]];
                return Frame(t == 0, false);
            });

            int stuck = 0, movedAfterStuck = 0, caughtUp = 0;
            for (int t = 1; t <= pullTicks; t++)
            {
                float step = (at[0, t] - at[0, t - 1]).Length.ToFloat();
                float planned = total[0] * (Simulation.MaelstromPullProgress(t, pullTicks)
                                            - Simulation.MaelstromPullProgress(t - 1, pullTicks)).ToFloat();
                Assert.LessOrEqual(step, planned * catchUp + 1e-3f, $"отставшее тело догоняет рывком — шаг больше {catchUp} плана, тик {t}");
                if (step < planned * .5f) stuck++;
                else if (stuck > 0)
                {
                    movedAfterStuck++;
                    if (step > planned * 1.2f) caughtUp++;
                }
            }
            Assert.Greater(stuck, 1, "камень тело не задержал — сценарий не проверяет упор");
            Assert.Greater(movedAfterStuck, 0, "тело так и не сошло с камня — сценарий не проверяет рывок после упора");
            Assert.Greater(caughtUp, 0, "сойдя с камня, тело не догоняет план");
            // Проверка 03.10: отставание в три тика догоняется к концу тяги — тело доезжает до прежней точки.
            Assert.AreEqual(stopAt[0], at[0, pullTicks].Length.ToFloat(), 1e-3f, "отставание не догнано");
            Assert.AreEqual(at[0, pullTicks], at[0, contact], "к контакту тело стоит");
            Assert.AreEqual(stopAt[1], at[1, pullTicks].Length.ToFloat(), 1e-4f, "свободное тело доезжает до прежней точки");
        }

        /// <summary>
        /// Корни держат тело 8 тиков середины тяги (тест возвращает его на место), потом
        /// отпускают. Столько не догнать: тело идёт не быстрее CatchUp × план до самого
        /// конца тяги — последний шаг тоже, — и недоезжает, а не прыгает к герою.
        /// </summary>
        [Test]
        public void Maelstrom_BodyHeldMostOfThePull_FollowsWhenFree_NoJumpAtTheEnd()
        {
            const int pullTicks = Simulation.MaelstromPullTicks, contact = Simulation.MaelstromContactDelayTicks;
            const int holdFrom = 5, holdTo = 12;
            float catchUp = Simulation.MaelstromPullCatchUp.ToFloat();
            Simulation sim = Arena(PelagForm.WhirlwindMaelstrom);
            int body = Enemy(sim, 3000, 0);
            float stopAt = (sim.Entities.BodyRadius[Simulation.PlayerId] + Fix64.Ratio(1, 2)
                            + sim.Entities.BodyRadius[body]).ToFloat();
            float total = 3f - stopAt;

            var at = new FixVec2[contact + 1];
            for (int t = 0; t <= contact; t++)
            {
                sim.Step(Frame(t == 0, false));
                if (t >= holdFrom && t <= holdTo) sim.Entities.Position[body] = at[holdFrom - 1];
                at[t] = sim.Entities.Position[body];
            }

            for (int t = holdTo + 1; t <= pullTicks; t++)
            {
                float step = (at[t] - at[t - 1]).Length.ToFloat();
                float planned = total * (Simulation.MaelstromPullProgress(t, pullTicks)
                                         - Simulation.MaelstromPullProgress(t - 1, pullTicks)).ToFloat();
                Assert.GreaterOrEqual(step, planned * .99f, $"отпущенное тело стоит, тик {t}");
                Assert.LessOrEqual(step, planned * catchUp + 1e-3f, $"отпущенное тело прыгает — шаг больше {catchUp} плана, тик {t}");
            }
            Assert.Greater(at[pullTicks].Length.ToFloat(), stopAt + .3f, "восемь тиков не догнать — тело недоезжает");
            Assert.Less(at[pullTicks].Length.ToFloat(), at[holdTo].Length.ToFloat() - total * .3f, "но отпущенное тянется дальше");
            Assert.AreEqual(at[pullTicks], at[contact], "к контакту тело стоит");
        }

        /// <summary>
        /// Водоворот собирает толпу роя (радиус 0,45; сценарии независимой проверки 03.10).
        /// Задние ряды, которых держат передние, догоняют план и встают у героя не дальше
        /// 15 см от прежнего волока с полным догоном (1,40–1,50 м), а не в ~2 м, как без
        /// догона; шаг заднего тела не больше CatchUp × план тика (полный догон давал
        /// последний шаг до 1,6 плана). Тот же сид и ввод — те же позиции.
        /// </summary>
        [Test]
        public void Maelstrom_GathersTheCrowd_BackRowsCatchUpWithoutAJerk()
        {
            // Двое на одном луче.
            CrowdGathers(new[] { (3000, 0), (3950, 0) }, new[] { 1 }, new[] { 1.40f });
            // Плотная пачка 4×2 с одной стороны: задние ряды — тела 4–7.
            var pack = new List<(int, int)>();
            for (int i = 0; i < 4; i++) for (int j = 0; j < 2; j++) pack.Add((2400 + i * 500, -500 + j * 1000));
            CrowdGathers(pack.ToArray(), new[] { 4, 5, 6, 7 }, new[] { 1.45f, 1.45f, 1.48f, 1.48f });
            // Дуга из шести на 3,2 м: тела 1–4 зажаты соседями.
            var arc = new List<(int, int)>();
            for (int i = 0; i < 6; i++)
            {
                double a = -0.6 + i * 0.24;
                arc.Add(((int)System.Math.Round(3200 * System.Math.Cos(a)), (int)System.Math.Round(3200 * System.Math.Sin(a))));
            }
            CrowdGathers(arc.ToArray(), new[] { 1, 2, 3, 4 }, new[] { 1.47f, 1.50f, 1.50f, 1.47f });
        }

        private static void CrowdGathers((int x, int y)[] spotsMm, int[] backRows, float[] fullCatchUpEnd)
        {
            const int pullTicks = Simulation.MaelstromPullTicks, contact = Simulation.MaelstromContactDelayTicks;
            float catchUp = Simulation.MaelstromPullCatchUp.ToFloat();
            FixVec2[,] at = CrowdPull(spotsMm, out float stopAt);
            FixVec2[,] again = CrowdPull(spotsMm, out _);
            for (int k = 0; k < spotsMm.Length; k++)
                for (int t = 0; t <= contact; t++)
                    Assert.AreEqual(at[k, t], again[k, t], $"тот же сид и ввод — другие позиции, тело {k}, тик {t}");

            for (int r = 0; r < backRows.Length; r++)
            {
                int k = backRows[r];
                float end = at[k, contact].Length.ToFloat();
                Assert.LessOrEqual(end, fullCatchUpEnd[r] + .15f,
                    $"толпа не собрана: тело {k} встало в {end:F2} м, полный догон — {fullCatchUpEnd[r]:F2}");
                Assert.GreaterOrEqual(end, stopAt - .05f, $"тело {k} протащило за точку остановки");
                float total = at[k, 0].Length.ToFloat() - stopAt;
                for (int t = 1; t <= pullTicks; t++)
                {
                    float step = (at[k, t] - at[k, t - 1]).Length.ToFloat();
                    float planned = total * (Simulation.MaelstromPullProgress(t, pullTicks)
                                             - Simulation.MaelstromPullProgress(t - 1, pullTicks)).ToFloat();
                    Assert.LessOrEqual(step, planned * catchUp + .01f, $"задний ряд догоняет рывком: тело {k}, тик {t}");
                }
            }
        }

        /// <summary>Тела роя в точках (мм), каст Водоворота; позиции по тикам до контакта (0 — тик каста).</summary>
        private static FixVec2[,] CrowdPull((int x, int y)[] spotsMm, out float stopAt)
        {
            const int contact = Simulation.MaelstromContactDelayTicks;
            Simulation sim = Arena(PelagForm.WhirlwindMaelstrom);
            var ids = new int[spotsMm.Length];
            for (int k = 0; k < spotsMm.Length; k++)
            {
                ids[k] = Enemy(sim, spotsMm[k].x, spotsMm[k].y);
                sim.Entities.BodyRadius[ids[k]] = Fix64.Ratio(45, 100);
            }
            stopAt = (sim.Entities.BodyRadius[Simulation.PlayerId] + Fix64.Ratio(1, 2) + Fix64.Ratio(45, 100)).ToFloat();
            var at = new FixVec2[spotsMm.Length, contact + 1];
            for (int t = 0; t <= contact; t++)
            {
                sim.Step(Frame(t == 0, false));
                for (int k = 0; k < ids.Length; k++) at[k, t] = sim.Entities.Position[ids[k]];
            }
            return at;
        }

        /// <summary>
        /// Проверка 03.10, раунд 2: тело держат до 13–15-го тика тяги и отпускают у самого конца.
        /// Последний тик делил весь остаток мимо догона — отпущенное на 16-м тике прыгало к
        /// герою на 52 см (догон 26,7). Теперь и последний шаг не больше CatchUp × план тика:
        /// точка конца подходит к телу, тело недоезжает.
        /// </summary>
        [TestCase(13)]
        [TestCase(14)]
        [TestCase(15)]
        public void Maelstrom_BodyHeldUntilTheLastTicks_LastStepNoJump(int holdTo)
        {
            const int pullTicks = Simulation.MaelstromPullTicks, contact = Simulation.MaelstromContactDelayTicks;
            const int holdFrom = 5;
            float catchUp = Simulation.MaelstromPullCatchUp.ToFloat();
            Simulation sim = Arena(PelagForm.WhirlwindMaelstrom);
            int body = Enemy(sim, 3000, 0);
            float stopAt = (sim.Entities.BodyRadius[Simulation.PlayerId] + Fix64.Ratio(1, 2)
                            + sim.Entities.BodyRadius[body]).ToFloat();
            float total = 3f - stopAt;

            var at = new FixVec2[contact + 1];
            for (int t = 0; t <= contact; t++)
            {
                sim.Step(Frame(t == 0, false));
                if (t >= holdFrom && t <= holdTo) sim.Entities.Position[body] = at[holdFrom - 1];
                at[t] = sim.Entities.Position[body];
            }

            for (int t = holdTo + 1; t <= pullTicks; t++)
            {
                float step = (at[t] - at[t - 1]).Length.ToFloat();
                float planned = total * (Simulation.MaelstromPullProgress(t, pullTicks)
                                         - Simulation.MaelstromPullProgress(t - 1, pullTicks)).ToFloat();
                Assert.GreaterOrEqual(step, planned * .99f, $"отпущенное тело стоит, тик {t}");
                Assert.LessOrEqual(step, planned * catchUp + 1e-3f, $"отпущенное тело прыгает — шаг больше {catchUp} плана, тик {t}");
            }
            Assert.Greater(at[pullTicks].Length.ToFloat(), stopAt + .3f, "столько не догнать — тело недоезжает");
            Assert.AreEqual(at[pullTicks], at[contact], "к контакту тело стоит");
            Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[body], "тяга кончилась");
        }

        /// <summary>
        /// Плотные пачки роя (проверка 03.10, раунд 2): на последнем тике тяги задние тела
        /// доезжали остатком целиком, шаг выходил за CatchUp × план. Теперь последний шаг
        /// каждого тела не больше догона (толчки соседей тоже в шаге); тот же сид и ввод —
        /// те же позиции.
        /// </summary>
        [Test]
        public void Maelstrom_DensePacks_LastTickWithinTheCatchUp()
        {
            DensePackLastTick(Grid(3, 3, 2000, -900, 900, 900));
            DensePackLastTick(Grid(4, 3, 2000, -900, 600, 900));
            DensePackLastTick(Grid(5, 2, 1900, -450, 450, 900));
            DensePackLastTick(Grid(4, 2, 2400, -500, 500, 1000));
        }

        private static (int, int)[] Grid(int rows, int columns, int x0, int y0, int dx, int dy)
        {
            var spots = new List<(int, int)>();
            for (int i = 0; i < rows; i++) for (int j = 0; j < columns; j++) spots.Add((x0 + i * dx, y0 + j * dy));
            return spots.ToArray();
        }

        private static void DensePackLastTick((int x, int y)[] spotsMm)
        {
            const int pullTicks = Simulation.MaelstromPullTicks, contact = Simulation.MaelstromContactDelayTicks;
            float catchUp = Simulation.MaelstromPullCatchUp.ToFloat();
            FixVec2[,] at = CrowdPull(spotsMm, out float stopAt);
            FixVec2[,] again = CrowdPull(spotsMm, out _);
            string pack = spotsMm.Length + " тел от " + spotsMm[0];
            for (int k = 0; k < spotsMm.Length; k++)
            {
                for (int t = 0; t <= contact; t++)
                    Assert.AreEqual(at[k, t], again[k, t], $"тот же сид и ввод — другие позиции, {pack}, тело {k}, тик {t}");
                float total = at[k, 0].Length.ToFloat() - stopAt;
                float step = (at[k, pullTicks] - at[k, pullTicks - 1]).Length.ToFloat();
                float planned = total * (Simulation.MaelstromPullProgress(pullTicks, pullTicks)
                                         - Simulation.MaelstromPullProgress(pullTicks - 1, pullTicks)).ToFloat();
                Assert.LessOrEqual(step, planned * catchUp + 1e-3f, $"последний тик — рывок, {pack}, тело {k}");
            }
        }

        [Test]
        public void Maelstrom_NoFormWhirlwindKeepsTheTenTickContact()
        {
            Simulation sim = Arena(PelagForm.None);
            Run(sim, 2, t => Frame(t == 0, false));
            Assert.AreEqual(Simulation.WhirlwindContactDelayTicks, sim.PlayerAction.ContactTick - sim.PlayerAction.StartTick);
            Assert.AreEqual(10, Simulation.WhirlwindContactDelayTicks);
            Assert.Greater(Simulation.MaelstromContactDelayTicks, Simulation.WhirlwindContactDelayTicks, "контакт Водоворота позже");
        }

        /// <summary>Водоворот в толпе живых мобов: свёртка хешей и позиций по тикам.</summary>
        private static ulong MaelstromCrowdScenario(out int pulled)
        {
            var sim = new Simulation(777, 128);
            sim.SetupKindTestArena(EnemyKind.ForestGuardian, 4, distance: Fix64.FromInt(3));
            FixVec2 hero = sim.Entities.Position[Simulation.PlayerId];
            for (int k = 0; k < 5; k++)
                sim.AddKindTestEnemy(EnemyKind.ForestRootSwarm,
                    hero + FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, 5)) * Fix64.Ratio(20 + k * 4, 10), 400);
            Give(sim, PelagForm.WhirlwindMaelstrom);
            pulled = 0;
            ulong fold = 14695981039346656037UL;
            for (int t = 0; t < 120; t++)
            {
                sim.Step(Frame(t == 3 || t == 70, false));
                sim.Entities.Health[Simulation.PlayerId] = sim.Entities.MaxHealth[Simulation.PlayerId];
                fold = Mix(fold, sim.StateHash());
                for (int i = 0; i < sim.Entities.Count; i++)
                {
                    fold = Mix(fold, (ulong)sim.Entities.Position[i].X.Raw);
                    fold = Mix(fold, (ulong)sim.Entities.Position[i].Y.Raw);
                }
                foreach (SimEvent ev in sim.Events)
                    if (ev.Type == SimEventType.WhirlwindMaelstromPull) pulled += ev.Amount;
            }
            return fold;
        }

        [Test]
        public void Maelstrom_EasedPull_SameSeedSameInput_SameHashesAndPositions()
        {
            ulong a = MaelstromCrowdScenario(out int pulledA);
            ulong b = MaelstromCrowdScenario(out int pulledB);
            Assert.AreEqual(a, b);
            Assert.AreEqual(pulledA, pulledB);
            Assert.Greater(pulledA, 2, "сценарий никого не потянул");
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
