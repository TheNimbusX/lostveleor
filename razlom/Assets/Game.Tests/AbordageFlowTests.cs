using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AbordageTests;

namespace Game.Tests
{
    /// <summary>Абордаж v2: посадка, самонаведение, пропавшая цель, срывы, взгляд, хеш (SPEC 2.10, тесты 3–8).</summary>
    public sealed class AbordageFlowTests
    {
        // ---------- 3. посадка ----------

        [TestCase(2500)]
        [TestCase(5000)]
        [TestCase(6900)]
        public void Landing_BodiesTenCentimetresApart_NoSlideAfterTheStrike(int distanceMm)
        {
            Simulation sim = Arena();
            int target = Enemy(sim, distanceMm, 0);
            List<Frame> frames = Cast(sim, target, 30);
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            FixVec2 landed = frames[strike].Position;
            double gap = Dist(landed, sim.Entities.Position[target]) - 0.85 - 0.45;
            Assert.That(gap, Is.EqualTo(0.1).Within(0.02), "зазор тел при посадке");
            for (int t = strike + 1; t <= strike + 6; t++)
                Assert.That(Dist(frames[t].Position, landed), Is.LessThanOrEqualTo(0.02), "после удара не едет, тик " + t);
        }

        // ---------- 4. самонаведение ----------

        [Test]
        public void Homing_TargetStrafesThreeMetresPerSecond_ThePunchStillLands()
        {
            Simulation sim = Arena();
            int target = Enemy(sim, 5000, 0);
            List<Frame> frames = Cast(sim, target, 20,
                before: t => { if (t > 0) sim.Entities.Position[target] += new FixVec2(Fix64.Zero, Mm(100)); });
            var punch = Of(frames, SimEventType.AbordagePunch)[0];
            Assert.IsTrue(punch.ev.Flag, "удар дошёл");
            Assert.AreEqual(target, punch.ev.Target);
            double gap = Dist(punch.frame.Position, PositionAt(frames, punch.frame.Tick, target)) - 0.85 - 0.45;
            Assert.That(gap, Is.LessThanOrEqualTo(0.3), "вплотную к ушедшей вбок цели");
        }

        private static FixVec2 PositionAt(List<Frame> frames, int tick, int target)
        {
            // Цель шла по +Y 0,1 м за тик с тика 1, до шага: на тике t она в (5, 0,1·t).
            return new FixVec2(Mm(5000), Mm(100 * tick));
        }

        [Test]
        public void Homing_TargetDashesAwayOverOneAndAHalfMetres_PullFreezes_PunchMisses()
        {
            Simulation sim = Arena();
            int target = Enemy(sim, 5000, 0);
            List<Frame> frames = Cast(sim, target, 20,
                before: t => { if (t == 8) sim.Entities.Position[target] += new FixVec2(Fix64.Zero, Mm(2500)); });
            var punch = Of(frames, SimEventType.AbordagePunch)[0];
            Assert.AreEqual(11, punch.frame.Tick, "срок удара прежний");
            Assert.IsFalse(punch.ev.Flag, "за рывком цели не гоняемся");
            Assert.IsTrue(frames[9].State.Frozen);
            Assert.That(punch.frame.Position.Y.ToDouble(), Is.EqualTo(0).Within(0.05), "точка посадки замерла");
        }

        // ---------- 5. цель пропала ----------

        [Test]
        public void TargetDiesInThePull_PunchGoesToTheNearestInReach_ElseIntoTheAir()
        {
            Simulation alone = Arena();
            int target = Enemy(alone, 5000, 0);
            List<Frame> air = Cast(alone, target, 20, before: t => { if (t == 8) alone.Entities.Alive[target] = false; });
            var miss = Of(air, SimEventType.AbordagePunch)[0];
            Assert.AreEqual(11, miss.frame.Tick);
            Assert.IsFalse(miss.ev.Flag, "в воздух");

            Simulation pair = Arena();
            int first = Enemy(pair, 5000, 0);
            int second = Enemy(pair, 4400, 1500, radiusMm: 450);
            List<Frame> frames = Cast(pair, first, 20, before: t => { if (t == 8) pair.Entities.Alive[first] = false; });
            var punch = Of(frames, SimEventType.AbordagePunch)[0];
            Assert.IsTrue(punch.ev.Flag);
            Assert.AreEqual(second, punch.ev.Target, "ближайший в досягаемости");
            Assert.Less(pair.Entities.Health[second], Health);
        }

        [Test]
        public void TargetDiesBeforeTheHook_AnchorRetargetsNearby_ElseRecall_NoTarget_Cooldown15()
        {
            Simulation sim = Arena();
            int target = Enemy(sim, 5000, 0);
            Fix64 before = sim.Entities.Lavidium[0];
            List<Frame> frames = Cast(sim, target, 20, before: t => { if (t == 4) sim.Entities.Alive[target] = false; });
            Assert.AreEqual(0, Of(frames, SimEventType.AbordageHook).Count, "зацепа нет");
            Assert.AreEqual(AbordagePhase.Recall, frames[4].State.Phase);
            var ended = Of(frames, SimEventType.AbordageEnded)[0];
            Assert.AreEqual(4 + Simulation.AbordageRecallTicks, ended.frame.Tick);
            Assert.AreEqual((int)AbordageEnd.NoTarget, ended.ev.Amount);
            Assert.AreEqual(4 + Simulation.AbordageRecallCooldownTicks, sim.AbilityReadyTick(Slot), "кулдаун 15 тиков");
            Assert.Less(sim.Entities.Lavidium[0].ToDouble(), before.ToDouble(), "цена потрачена");

            Simulation pair = Arena();
            int first = Enemy(pair, 5000, 0);
            int near = Enemy(pair, 5400, 1600, radiusMm: 450);
            List<Frame> retarget = Cast(pair, first, 25, before: t => { if (t == 4) pair.Entities.Alive[first] = false; });
            var punch = Of(retarget, SimEventType.AbordagePunch)[0];
            Assert.AreEqual(near, punch.ev.Target, "перевыбор в 1,5 м от места цели");
            Assert.IsTrue(punch.ev.Flag);
        }

        // ---------- 6. срывы ----------

        [Test]
        public void Dash_Stun_AndForeignKnockback_BreakTheAbordage()
        {
            Simulation dash = Arena();
            dash.SetAbility(1, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            int a = Enemy(dash, 5000, 0);
            List<Frame> f1 = Run(dash, 15, t => t == 0 ? Press(a) : t == 8 ? DashPress(dash) : InputFrame.Empty);
            AssertInterruptedAt(f1, 8);
            Assert.AreEqual(Health, dash.Entities.Health[a], "кулака не было");

            Simulation stun = Arena();
            int b = Enemy(stun, 5000, 0);
            List<Frame> f2 = Cast(stun, b, 15, before: t => { if (t == 8) stun.Statuses.ApplyStun(0, stun.Tick + 10); });
            AssertInterruptedAt(f2, 8);
            Assert.IsFalse(ForcedMotion.IsActive(stun.Entities, 0), "тяга снята");

            Simulation push = Arena();
            int c = Enemy(push, 5000, 0);
            List<Frame> f3 = Cast(push, c, 15, before: t =>
            {
                if (t == 8) ForcedMotion.Begin(push.Entities, 0, push.Entities.Position[0] + new FixVec2(Fix64.Zero, Fix64.One), 4,
                    ForcedMotionKind.Knockback);
            });
            AssertInterruptedAt(f3, 8);
            Assert.AreEqual(1.0, push.Entities.Position[0].Y.ToDouble(), 0.01, "чужой отброс доехал");
        }

        private static InputFrame DashPress(Simulation sim)
        {
            InputFrame input = Press(-1, slot: 1);
            input.Aim = sim.Entities.Position[0] + new FixVec2(Fix64.Zero, Fix64.FromInt(3));
            return input;
        }

        private static void AssertInterruptedAt(List<Frame> frames, int tick)
        {
            var ended = Of(frames, SimEventType.AbordageEnded);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(tick, ended[0].frame.Tick);
            Assert.AreEqual((int)AbordageEnd.Interrupted, ended[0].ev.Amount);
            Assert.AreEqual(0, Of(frames, SimEventType.AbordagePunch).Count);
        }

        [Test]
        public void OtherAbility_BeforeTheStrike_IsBuffered_AndCastsTheTickAfter()
        {
            Simulation sim = Arena();
            sim.SetAbility(1, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            int target = Enemy(sim, 5000, 0);
            List<Frame> frames = Run(sim, 16, t => t == 0 ? Press(target) : t == 7 ? Press(-1, slot: 1) : InputFrame.Empty);
            Assert.AreEqual(11, TickOf(frames, SimEventType.AbordagePunch));
            var casts = Of(frames, SimEventType.AbilityCast);
            Assert.AreEqual(2, casts.Count);
            Assert.AreEqual(1, casts[1].ev.Amount, "Вихрь");
            Assert.AreEqual(12, casts[1].frame.Tick, "из буфера — тиком после удара");
            Assert.Less(sim.Entities.Health[target], Health, "кулак успел");
        }

        [Test]
        public void Walking_IsHeldUntilTheSecondExitTick_ThenBreaksTheExit()
        {
            Simulation sim = Arena();
            int target = Enemy(sim, 5000, 0);
            var walk = InputFrame.Empty;
            walk.Flags = (byte)InputFlags.MoveOrder;
            walk.Aim = new FixVec2(Fix64.Zero, Fix64.FromInt(-5));
            walk.AttackTarget = walk.AbilityTarget = -1;
            List<Frame> frames = Run(sim, 20, t => t == 0 ? Press(target) : t > 11 ? walk : InputFrame.Empty);
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            int free = strike + Simulation.AbordageHoldTicks + Simulation.AbordageExitLockedTicks;
            for (int t = strike; t < free; t++)
                Assert.That(Dist(frames[t].Position, frames[strike].Position), Is.LessThan(1e-6), "держит, тик " + t);
            var ended = Of(frames, SimEventType.AbordageEnded)[0];
            Assert.AreEqual(free, ended.frame.Tick);
            Assert.AreEqual((int)AbordageEnd.WalkedOut, ended.ev.Amount);
            Assert.Greater(Dist(frames[free].Position, frames[strike].Position), 0.01, "пошёл");
        }

        [Test]
        public void Sabre_IsHeldUntilTheExitCanBeWalkedOutOf()
        {
            Simulation sim = Arena();
            int target = Enemy(sim, 5000, 0);
            List<Frame> frames = Run(sim, 30, t =>
            {
                if (t == 0) return Press(target);
                var attack = InputFrame.Empty;
                attack.Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack);
                attack.Aim = sim.Entities.Position[target];
                attack.AttackTarget = attack.AbilityTarget = -1;
                return attack;
            });
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            Assert.IsTrue(Of(frames, SimEventType.AbordagePunch)[0].ev.Flag, "зажатая атака не сорвала Абордаж");
            var swings = Of(frames, SimEventType.Attack);
            Assert.Greater(swings.Count, 0);
            Assert.AreEqual(strike + Simulation.AbordageHoldTicks + Simulation.AbordageExitLockedTicks, swings[0].frame.Tick,
                "первый удар сабли — с хвоста выхода, не раньше");
        }

        // ---------- 7. взгляд ----------

        [Test]
        public void Facing_OnTheTargetAtCast_AlongThePullAtTheEnd_NoJumpOnTheEndTick()
        {
            Simulation sim = Arena();
            int target = Enemy(sim, 3500, 3500);
            sim.Entities.Facing[0] = new FixVec2(-Fix64.One, Fix64.Zero);
            List<Frame> frames = Cast(sim, target, 30);
            FixVec2 toTarget = (sim.Entities.Position[target] - frames[0].Position).Normalized();
            Assert.Greater(FixVec2.Dot(frames[0].Facing, toTarget).ToDouble(), 0.999, "в каст — на цель");
            var hook = Of(frames, SimEventType.AbordageHook)[0];
            FixVec2 pull = (hook.ev.Position - hook.frame.Position).Normalized();
            int end = TickOf(frames, SimEventType.AbordageEnded);
            Assert.Greater(FixVec2.Dot(frames[end].Facing, pull).ToDouble(), 0.999, "в конце — по тяге");
            for (int t = end; t < frames.Count; t++)
                Assert.Greater(FixVec2.Dot(frames[t].Facing, frames[end].Facing).ToDouble(), 0.9999, "без скачка, тик " + t);
        }

        // ---------- 8. хеш ----------

        [TestCase(PelagForm.None)]
        [TestCase(PelagForm.AbordageQuake)]
        [TestCase(PelagForm.AbordageGeyser)]
        [TestCase(PelagForm.AbordageBreach)]
        public void TwoIdenticalRuns_WithAbordage_HashTheSameEveryTick(PelagForm form)
        {
            List<Frame> Once()
            {
                Simulation sim = Arena(form, 1234, Talent(2));
                int a = Enemy(sim, 5000, 0, radiusMm: 450);
                Enemy(sim, 6000, 1500, radiusMm: 450);
                Enemy(sim, 7200, -300, radiusMm: 450);
                Enemy(sim, 3600, 2200, radiusMm: 450);
                return Run(sim, 60, t => t == 0 || t == 13 ? Press(a) : InputFrame.Empty,
                    before: t => sim.Entities.Position[a] += new FixVec2(Fix64.Zero, Mm(t % 7 == 0 ? 60 : 0)));
            }
            List<Frame> first = Once(), second = Once();
            for (int t = 0; t < first.Count; t++) Assert.AreEqual(first[t].Hash, second[t].Hash, form + ", тик " + t);
            Assert.AreEqual(2, Of(first, SimEventType.AbordagePunch).Count, "оба заряда ударили");
            Assert.AreNotEqual(first[0].Hash, first[13].Hash);
        }
    }
}
