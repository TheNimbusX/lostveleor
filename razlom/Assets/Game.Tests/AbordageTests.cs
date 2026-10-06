using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Абордаж v2 (Simulation.Abordage, спека artifacts/abordage/plan/SPEC.md, тесты 2.10):
    /// цель как у Шквала, тайминг по длине, посадка вплотную. Ход каста —
    /// AbordageFlowTests, формы и таланты — AbordageFormTests.
    /// </summary>
    public sealed class AbordageTests
    {
        internal const int Slot = 0;
        internal const int Health = 100000;

        internal static Simulation Arena(PelagForm form = PelagForm.None, ulong seed = 1234, params AbilityNode[] extra)
        {
            var sim = new Simulation(seed, 128);
            sim.SetupTestArena(0);
            Give(sim, form, extra);
            sim.Entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(sim.Entities.MaxLavidium[Simulation.PlayerId]);
            return sim;
        }

        internal static void Give(Simulation sim, PelagForm form, params AbilityNode[] extra)
        {
            var nodes = new AbilityNode[RunLoadout.MaxNodesPerSlot];
            int count = PelagForms.AppendFormNodes(form, nodes, 0);
            foreach (AbilityNode node in extra) nodes[count++] = node;
            sim.SetAbility(Slot, AbilityDefinition.AnchorLeap(), nodes, count);
        }

        /// <summary>Узел таланта линии Абордажа (0 — «Длинная цепь», 1 — «Тяжёлый кулак», 2 — «Два заряда»…).</summary>
        internal static AbilityNode Talent(int index)
        {
            var buffer = new AbilityNode[1];
            SabreTalents.AppendNode(SabreTalentLine.Boarding, index, buffer, 0);
            return buffer[0];
        }

        internal static Fix64 Mm(int mm) => Fix64.Ratio(mm, 1000);

        /// <summary>Неподвижный враг без удара в (x, y) мм; тело — как у Хранителя (0,85 м).</summary>
        internal static int Enemy(Simulation sim, int xMm, int yMm, int health = Health, int radiusMm = 850)
        {
            int id = sim.Entities.Spawn(new FixVec2(Mm(xMm), Mm(yMm)), health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Mm(radiusMm);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        internal static InputFrame Press(int target, int slot = Slot)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.AttackTarget = -1;
            input.AbilityTarget = target;
            return input;
        }

        internal struct Frame
        {
            public int Tick;
            public SimEvent[] Events;
            public FixVec2 Position, Facing;
            public AbordageState State;
            public ulong Hash;
        }

        internal static List<Frame> Run(Simulation sim, int ticks, System.Func<int, InputFrame> input, System.Action<int> before = null)
        {
            var frames = new List<Frame>();
            for (int t = 0; t < ticks; t++)
            {
                before?.Invoke(t);
                int tick = sim.Tick;
                sim.Step(input(t));
                var events = new SimEvent[sim.Events.Count];
                for (int i = 0; i < events.Length; i++) events[i] = sim.Events[i];
                frames.Add(new Frame
                {
                    Tick = tick, Events = events, Position = sim.Entities.Position[Simulation.PlayerId],
                    Facing = sim.Entities.Facing[Simulation.PlayerId], State = sim.Abordage, Hash = sim.StateHash(),
                });
            }
            return frames;
        }

        internal static List<Frame> Cast(Simulation sim, int target, int ticks = 40, System.Action<int> before = null)
            => Run(sim, ticks, t => t == 0 ? Press(target) : InputFrame.Empty, before);

        internal static List<(Frame frame, SimEvent ev)> Of(List<Frame> frames, SimEventType type)
        {
            var found = new List<(Frame, SimEvent)>();
            foreach (Frame f in frames)
                foreach (SimEvent ev in f.Events)
                    if (ev.Type == type) found.Add((f, ev));
            return found;
        }

        internal static int TickOf(List<Frame> frames, SimEventType type)
        {
            var found = Of(frames, type);
            Assert.That(found.Count, Is.EqualTo(1), type + " — ровно одно событие");
            return found[0].frame.Tick;
        }

        internal static double Dist(FixVec2 a, FixVec2 b) => FixVec2.Distance(a, b).ToDouble();

        internal static double Gap(Simulation sim, int target)
            => Dist(sim.Entities.Position[Simulation.PlayerId], sim.Entities.Position[target])
               - sim.Entities.BodyRadius[target].ToDouble() - sim.Entities.BodyRadius[Simulation.PlayerId].ToDouble();

        // ---------- 1. выбор цели ----------

        [Test]
        public void Pick_Hovered_ThenNearestToCursor_ThenCone_ElseNone()
        {
            Simulation sim = Arena();
            int a = Enemy(sim, 4000, 0, radiusMm: 450);
            int b = Enemy(sim, 3000, 2600, radiusMm: 450);
            AbilityBuild build = sim.GetAbility(Slot);
            FixVec2 hero = sim.Entities.Position[Simulation.PlayerId];

            Assert.AreEqual(b, sim.PickAbordageTarget(b, new FixVec2(Mm(4000), Fix64.Zero), hero, build), "наведённый — первым");
            // Курсор в 1,2 м от края a (центр 1,65), b дальше 1,5 по краю: правило 2 — a.
            Assert.AreEqual(a, sim.PickAbordageTarget(-1, new FixVec2(Mm(5650), Fix64.Zero), hero, build));
            // Курсор далеко за a по лучу (никого в 1,5 м) — конус ±30° от героя: a на луче.
            Assert.AreEqual(a, sim.PickAbordageTarget(-1, new FixVec2(Mm(20000), Mm(1000)), hero, build));
            // Луч на 90° от обоих — никого.
            Assert.AreEqual(-1, sim.PickAbordageTarget(-1, new FixVec2(Fix64.Zero, Mm(-9000)), hero, build));
            // Вне дальности (7 м) и мёртвый — не цель.
            int far = Enemy(sim, 9000, 0, radiusMm: 450);
            Assert.AreNotEqual(far, sim.PickAbordageTarget(far, new FixVec2(Mm(9000), Fix64.Zero), hero, build));
            Assert.IsFalse(sim.ValidAbilityTarget(far, build), "9 м при цепи 7 м");
            sim.Entities.Alive[a] = false;
            Assert.IsFalse(sim.ValidAbilityTarget(a, build));
            Assert.AreNotEqual(a, sim.PickAbordageTarget(a, new FixVec2(Mm(4000), Fix64.Zero), hero, build));
        }

        [Test]
        public void Pick_IsPure_TiesGoToTheLowerIndex()
        {
            Simulation sim = Arena();
            int a = Enemy(sim, 3000, 1000, radiusMm: 450);
            int b = Enemy(sim, 3000, -1000, radiusMm: 450);
            ulong before = sim.StateHash();
            int pick = sim.PickAbordageTarget(-1, new FixVec2(Mm(3000), Fix64.Zero), sim.Entities.Position[0], sim.GetAbility(Slot));
            Assert.AreEqual(System.Math.Min(a, b), pick, "ничья — младший индекс");
            Assert.AreEqual(before, sim.StateHash(), "выбор цели ничего не пишет");
        }

        [Test]
        public void Cast_WithoutAValidTarget_CostsNothing_NoCooldown_WhirlwindKeepsSpinning()
        {
            Simulation sim = Arena();
            sim.SetAbility(1, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            int far = Enemy(sim, 9000, 0);
            sim.Step(Press(-1, slot: 1));
            Fix64 lavidium = sim.Entities.Lavidium[0];
            int serial = sim.PlayerAction.Serial;
            sim.Step(Press(-1));
            sim.Step(Press(far));   // враг в 9 м — дальше цепи
            // Лавидий за эти тики только копится (восстановление), цена 15 не списана.
            Assert.GreaterOrEqual(sim.Entities.Lavidium[0].ToDouble(), lavidium.ToDouble(), "цена не списана");
            Assert.AreEqual(0, sim.AbilityReadyTick(Slot), "кулдауна нет");
            Assert.AreEqual(serial, sim.PlayerAction.Serial, "Вихрь не сорван");
            Assert.IsFalse(sim.PlayerAction.Interrupted);
            Assert.IsFalse(sim.AbordageActive);
        }

        [Test]
        public void Target_BehindAWall_IsNotATarget()
        {
            var modules = PrototypeContent.Modules();
            var settings = new RiftLevelSettings(24, 1, 1, 2, 1, 3, 100, solidEnvironment: true);
            Fix64 body = EntityStore.DefaultBodyRadius;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var map = new LayoutMap(modules, 64);
                settings.Generate(new LayoutGenerator(), modules, map, seed);
                for (int i = 0; i < map.ObstacleCount; i++)
                {
                    var obstacle = map.GetObstacle(i);
                    var offset = new FixVec2(obstacle.Radius + body + Fix64.Ratio(2, 10), Fix64.Zero);
                    FixVec2 a = obstacle.Center - offset, b = obstacle.Center + offset + new FixVec2(Fix64.FromInt(2), Fix64.Zero);
                    FixVec2 open = a - new FixVec2(Fix64.FromInt(3), Fix64.Zero);
                    if (FixVec2.Distance(a, b) > Fix64.FromInt(6)) continue;
                    if (!map.IsWalkable(a, body) || !map.IsWalkable(b, body) || !map.IsWalkable(open, body)) continue;
                    if (!map.IsWalkable(b - new FixVec2(Mm(1000), Fix64.Zero), body) || map.CanTravel(a, b, body)) continue;
                    if (!map.CanTravel(a, open, body)) continue;

                    var sim = new Simulation(seed, 512);
                    settings.Spawn(sim, map, seed);
                    for (int k = 1; k < sim.Entities.Count; k++) sim.Entities.Alive[k] = false;
                    Give(sim, PelagForm.None);
                    sim.Entities.Position[0] = a;
                    int behind = sim.Entities.Spawn(b, Health, Faction.Orvill);
                    int clear = sim.Entities.Spawn(open, Health, Faction.Orvill);
                    AbilityBuild build = sim.GetAbility(Slot);
                    Assert.IsFalse(sim.ValidAbilityTarget(behind, build), "за стеной — не цель, сид " + seed);
                    Assert.IsTrue(sim.ValidAbilityTarget(clear, build), "в открытую — цель, сид " + seed);
                    Assert.AreEqual(-1, sim.PickAbordageTarget(behind, b, a, build), "наведённый за стеной — никого");
                    return;
                }
            }
            Assert.Inconclusive("не нашлось препятствия для сцены");
        }

        // ---------- 2. таймлайн ----------

        // Замах 3 (через плечо, владелец 03.10; было 2): всё после выпуска на тик позже.
        [TestCase(2000, 1, 2, 6)]
        [TestCase(3500, 2, 3, 8)]
        [TestCase(5000, 3, 5, 11)]
        [TestCase(7000, 4, 8, 15)]
        public void Timeline_ThrowAtThree_HookAfterTheAnchor_StrikeOnArrival(int distanceMm, int hook, int pull, int strike)
        {
            Simulation sim = Arena();
            int target = Enemy(sim, distanceMm, 0);
            List<Frame> frames = Cast(sim, target, 30);
            Assert.AreEqual(0, TickOf(frames, SimEventType.AbilityCast));
            var thrown = Of(frames, SimEventType.AbordageThrow);
            Assert.AreEqual(3, thrown[0].frame.Tick, "выпуск якоря");
            Assert.AreEqual(hook, thrown[0].ev.Amount, "тиков полёта якоря");
            var hooked = Of(frames, SimEventType.AbordageHook);
            Assert.AreEqual(3 + hook, hooked[0].frame.Tick, "зацеп");
            Assert.AreEqual(pull, hooked[0].ev.Amount, "тиков тяги");
            var punch = Of(frames, SimEventType.AbordagePunch);
            Assert.AreEqual(strike, punch[0].frame.Tick, "удар в тик прибытия");
            Assert.IsTrue(punch[0].ev.Flag);
            Assert.AreEqual(strike + AbordageTestsTail, TickOf(frames, SimEventType.AbordageEnded), "конец: удар + 3 + 6");
            Assert.AreEqual(strike, frames[0].State.ArriveTick, "прогноз в каст совпал");
            // Тело стоит до тика после зацепа.
            for (int t = 0; t <= 3 + hook; t++) Assert.AreEqual(0.0, frames[t].Position.X.ToDouble(), 1e-6, "тело стоит, тик " + t);
            Assert.Greater(frames[4 + hook].Position.X.ToDouble(), 0.3, "тело тронулось тиком после зацепа");
        }

        private const int AbordageTestsTail = Simulation.AbordageHoldTicks + Simulation.AbordageExitTicks;

        [Test]
        public void Timeline_LongChainNineMetres_StrikeAtTwentyOne()
        {
            Simulation sim = Arena(PelagForm.None, 1234, Talent(0));
            Assert.AreEqual(9.0, sim.GetAbility(Slot).Get(AbilityStatType.Radius).ToDouble(), 1e-6);
            int target = Enemy(sim, 9000, 0);
            List<Frame> frames = Cast(sim, target, 35);
            Assert.AreEqual(9, TickOf(frames, SimEventType.AbordageHook));
            Assert.AreEqual(21, TickOf(frames, SimEventType.AbordagePunch));
        }

        // ---------- 2а. цель за спиной: замах на тик дольше (владелец 03.10) ----------

        /// <summary>Враг в distanceMm под углом angleDeg от взгляда героя (герой в нуле, смотрит по +X).</summary>
        private static int EnemyAtAngle(Simulation sim, double angleDeg, int distanceMm = 5000)
        {
            double r = angleDeg * System.Math.PI / 180.0;
            return Enemy(sim, (int)System.Math.Round(distanceMm * System.Math.Cos(r)),
                (int)System.Math.Round(distanceMm * System.Math.Sin(r)));
        }

        [TestCase(0.0, 3)]
        [TestCase(60.0, 3)]
        [TestCase(89.0, 3)]
        [TestCase(-89.0, 3)]
        [TestCase(91.0, 4)]
        [TestCase(-91.0, 4)]
        [TestCase(135.0, 4)]
        [TestCase(180.0, 4)]
        public void Windup_TargetBehind_OneTickLonger_EverythingAfterShiftsByATick(double angleDeg, int windup)
        {
            Simulation sim = Arena();
            int target = EnemyAtAngle(sim, angleDeg);
            List<Frame> frames = Cast(sim, target, 30);
            int shift = windup - Simulation.AbordageWindupTicks;
            // 5 м: выпуск 3, зацеп 6, удар 11, конец 20 (SPEC 2.2, замах через плечо 03.10) — за спиной всё на тик позже.
            Assert.AreEqual(0, TickOf(frames, SimEventType.AbilityCast));
            Assert.AreEqual(windup, frames[0].State.WindupTicks, "замах в снимке");
            Assert.AreEqual(windup, frames[0].State.ReleaseTick, "прогноз выпуска");
            Assert.AreEqual(11 + shift, frames[0].State.ArriveTick, "прогноз удара в каст");
            Assert.AreEqual(windup, TickOf(frames, SimEventType.AbordageThrow), "выпуск");
            Assert.AreEqual(6 + shift, TickOf(frames, SimEventType.AbordageHook), "зацеп");
            var punch = Of(frames, SimEventType.AbordagePunch);
            Assert.AreEqual(11 + shift, punch[0].frame.Tick, "удар в тик прибытия");
            Assert.IsTrue(punch[0].ev.Flag, "удар дошёл");
            Assert.AreEqual(20 + shift, TickOf(frames, SimEventType.AbordageEnded), "конец: удар + 3 + 6");
            for (int t = 0; t <= 6 + shift; t++)
                Assert.That(Dist(frames[t].Position, FixVec2.Zero), Is.LessThan(1e-6), "тело стоит до тика после зацепа, тик " + t);
            FixVec2 toTarget = (sim.Entities.Position[target] - frames[0].Position).Normalized();
            Assert.Greater(FixVec2.Dot(frames[0].Facing, toTarget).ToDouble(), 0.999, "взгляд Sim на цель сразу в каст");

            // Часы действия в каст — по сдвинутому прогнозу.
            Simulation clock = Arena();
            int again = EnemyAtAngle(clock, angleDeg);
            clock.Step(Press(again));
            Assert.AreEqual(11 + shift, clock.PlayerAction.ContactTick, "контакт на часах");
            Assert.AreEqual(20 + shift, clock.PlayerAction.EndTick, "конец на часах");
        }

        [Test]
        public void Windup_JudgedByTheFacingBeforeTheCast_ExactlyNinetyIsNotBehind()
        {
            int Windup(FixVec2 facing, bool fastAbilities = false)
            {
                Simulation sim = Arena();
                if (fastAbilities)
                {
                    sim.Entities.Stats[Simulation.PlayerId].SetBase(StatType.AbilitySpeed, Fix64.One);
                    sim.Entities.RefreshStats(Simulation.PlayerId);
                }
                int target = Enemy(sim, 5000, 0);
                sim.Entities.Facing[Simulation.PlayerId] = facing;
                sim.Step(Press(target));
                Assert.AreEqual(AbordagePhase.Windup, sim.Abordage.Phase);
                Assert.AreEqual(sim.Abordage.CastTick + sim.Abordage.WindupTicks, sim.Abordage.ReleaseTick);
                return sim.Abordage.WindupTicks;
            }
            Assert.AreEqual(3, Windup(new FixVec2(Fix64.One, Fix64.Zero)), "впереди");
            Assert.AreEqual(3, Windup(new FixVec2(Fix64.Zero, Fix64.One)), "ровно 90° — не за спиной");
            Assert.AreEqual(4, Windup(new FixVec2(-Mm(18), Fix64.One)), "91°");
            Assert.AreEqual(4, Windup(new FixVec2(-Fix64.One, Fix64.Zero)), "180°");
            Assert.AreEqual(3, Windup(new FixVec2(-Fix64.One, Fix64.Zero), fastAbilities: true),
                "темп способностей сжимает замах 3 до 2, не короче — тик разворота остаётся");
            Assert.IsFalse(Simulation.AbordageTargetBehind(FixVec2.Zero, new FixVec2(-Fix64.One, Fix64.Zero)),
                "без взгляда — обычный замах");
        }

        [Test]
        public void Windup_TargetBehind_TwoIdenticalRunsHashTheSame_TheHashSeesTheWindup()
        {
            List<Frame> Once()
            {
                Simulation sim = Arena(PelagForm.AbordageQuake);
                int a = EnemyAtAngle(sim, 180.0);
                Enemy(sim, -3000, 2500, radiusMm: 450);
                return Run(sim, 40, t => t == 0 ? Press(a) : InputFrame.Empty,
                    before: t => sim.Entities.Position[a] += new FixVec2(Fix64.Zero, Mm(t % 5 == 0 ? 40 : 0)));
            }
            List<Frame> first = Once(), second = Once();
            Assert.AreEqual(4, first[0].State.WindupTicks, "цель за спиной");
            Assert.AreEqual(1, Of(first, SimEventType.AbordagePunch).Count, "удар был");
            for (int t = 0; t < first.Count; t++) Assert.AreEqual(first[t].Hash, second[t].Hash, "тик " + t);

            AbordageState state = first[0].State, shorter = state;
            shorter.WindupTicks = Simulation.AbordageWindupTicks;
            ulong a1 = 1469598103934665603UL, a2 = a1;
            state.HashInto(ref a1);
            shorter.HashInto(ref a2);
            Assert.AreNotEqual(a1, a2, "замах входит в хеш");
        }

        [Test]
        public void PullStep_IsLengthOverTicks_RoundedLikeTheSquall()
        {
            for (int mm = 2400; mm <= 8800; mm += 150)
            {
                // «Длинная цепь» (9 м): длинные тяги тоже по правилу.
                Simulation sim = Arena(PelagForm.None, 1234, Talent(0));
                int target = Enemy(sim, mm, 0);
                List<Frame> frames = Cast(sim, target, 26);
                var hook = Of(frames, SimEventType.AbordageHook)[0];
                double length = Dist(hook.ev.Position, hook.frame.Position);
                double step = length / hook.ev.Amount;
                Assert.That(step, Is.InRange(0.49, 0.83), "шаг тяги, цель в " + mm + " мм");
                if (length >= 3.0) Assert.That(step, Is.InRange(0.59, 0.73), "от 3 м тяги, цель в " + mm + " мм");
                for (int t = hook.frame.Tick + 1; t <= hook.frame.Tick + hook.ev.Amount; t++)
                    Assert.That(Dist(frames[t].Position, frames[t - 1].Position), Is.EqualTo(step).Within(0.02), "ровный шаг, тик " + t);
            }
        }
    }
}
