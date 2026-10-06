using System;
using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Хозяин Чащи — твёрдое тело (владелец 02.10: «сквозь босса не должны иметь
    /// возможность проходить, только дешем»): корпус из семи кругов
    /// (Simulation.ForestBoss.Hull) держит героя и подмогу, рывок проходит
    /// насквозь, в нырке корпуса нет, удары героя достают от любой части корпуса.
    /// Стенд — как в ThicketMasterTests: SetupKindTestArena на арене 9, герой в
    /// (0, 0), босс в 6 м по +X лицом к герою; поле без стен.
    /// </summary>
    public sealed class ThicketMasterHullTests
    {
        private const int Boss = 1;
        private const int IntroDone = Simulation.ThicketMinSleepTicks + Simulation.ThicketWakeTicks
            + Simulation.ThicketRoarWindupTicks + Simulation.ThicketRoarRecoveryTicks;   // 180
        private const double Tolerance = 0.001;

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        private static Simulation Arena(double distance = 6, bool walks = false)
        {
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromDouble(distance));
            var e = sim.Entities;
            e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
            e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            e.RefreshStats(0);
            e.Health[0] = e.MaxHealth[0];
            if (!walks)
            {
                e.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
                e.RefreshStats(Boss);
            }
            return sim;
        }

        /// <summary>Нырок (сближение и «под героя», 03.10), касты и буря — на перезарядке до конца теста (кроме allowed).</summary>
        private static void Only(Simulation sim, params ThicketMasterAction[] allowed)
        {
            var specials = new[]
            {
                ThicketMasterAction.Dive, ThicketMasterAction.Sprout, ThicketMasterAction.Pollen, ThicketMasterAction.Rain,
                ThicketMasterAction.Storm,
            };
            foreach (var action in specials)
                if (Array.IndexOf(allowed, action) < 0) sim.SetThicketReadyTick(Boss, action, int.MaxValue / 2);
            if (Array.IndexOf(allowed, ThicketMasterAction.Dive) < 0) sim.SetThicketDiveDueTick(Boss, int.MaxValue / 2);
        }

        private static void Until(Simulation sim, int tick)
        {
            while (sim.Tick < tick) sim.Step(InputFrame.Empty);
        }

        /// <summary>Зазор между телом id и корпусом (меньше нуля — внутри).</summary>
        private static double Gap(Simulation sim, int id)
            => (sim.ThicketHullGap(Boss, sim.Entities.Position[id]) - sim.Entities.BodyRadius[id]).ToDouble();

        /// <summary>
        /// Зазор тела id до кругов, которые сейчас держат: в замахе лапы передние лапы подняты
        /// (Simulation.ThicketPawsLifted, баланс 02.10, ночь) — под ними стоять можно.
        /// </summary>
        private static double HoldingGap(Simulation sim, int id)
        {
            double gap = double.MaxValue;
            bool lifted = sim.ThicketPawsLifted(Boss);
            for (int k = 0; k < Simulation.ThicketHullCircleCount; k++)
            {
                if (lifted && (k == 1 || k == 2)) continue;
                if (!sim.TryGetThicketHullCircle(Boss, k, out var center, out var radius)) continue;
                gap = Math.Min(gap, (FixVec2.Distance(sim.Entities.Position[id], center) - radius - sim.Entities.BodyRadius[id]).ToDouble());
            }
            return gap;
        }

        private static FixVec2 Forward(Simulation sim) => sim.Entities.Facing[Boss].Normalized();

        private static FixVec2 Left(Simulation sim)
        {
            var f = Forward(sim);
            return new FixVec2(-f.Y, f.X);
        }

        /// <summary>Точка в осях босса: forward — вперёд по взгляду, side — влево.</summary>
        private static FixVec2 Local(Simulation sim, double forward, double side)
            => sim.Entities.Position[Boss] + Forward(sim) * Fix64.FromDouble(forward) + Left(sim) * Fix64.FromDouble(side);

        /// <summary>Место вплотную к кругу index снаружи по лучу от центра босса через центр круга.</summary>
        private static FixVec2 AgainstCircle(Simulation sim, int index, double extra = 0.01)
        {
            var boss = sim.Entities.Position[Boss];
            var center = sim.ThicketHullCircleCenter(Boss, index);
            Simulation.ThicketHullLocal(index, out _, out _, out var radius);
            var direction = (center - boss).Normalized();
            return center + direction * (radius + sim.Entities.BodyRadius[0] + Fix64.FromDouble(extra));
        }

        private static InputFrame Walk(FixVec2 to)
        {
            var input = InputFrame.Empty;
            input.Flags = (byte)InputFlags.MoveOrder;
            input.Aim = to;
            return input;
        }

        private static InputFrame Dash(FixVec2 aim)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << PelagKit.DashSlot);
            input.Aim = aim;
            return input;
        }

        private static int BossDamage(Simulation sim, DamageOrigin? origin = null)
        {
            int total = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.Damage && e.Source == Simulation.PlayerId && e.Target == Boss
                    && (origin == null || e.DamageOrigin == origin.Value)) total += e.Amount;
            return total;
        }

        // ---------- корпус ----------

        [Test]
        public void Hull_SevenCircles_FromTheModel_TouchingTheChestIsTheHoldDistance()
        {
            var sim = Arena();
            for (int k = 0; k < Simulation.ThicketHullCircleCount; k++)
                Assert.IsTrue(sim.TryGetThicketHullCircle(Boss, k, out _, out _), "круг " + k);
            Assert.IsFalse(sim.TryGetThicketHullCircle(Boss, Simulation.ThicketHullCircleCount, out _, out _));
            // Модель 4,14 м: грудь до 2,74 м вперёд, лапы до 2,27 м вбок, корень хвоста до 2,04 м назад.
            Assert.That(sim.ThicketHullGap(Boss, Local(sim, 2.74, 0)).ToDouble(), Is.InRange(-0.01, 0.01), "грудь");
            Assert.That(sim.ThicketHullGap(Boss, Local(sim, 1.978, 2.266)).ToDouble(), Is.InRange(-0.01, 0.01), "левая лапа");
            Assert.That(sim.ThicketHullGap(Boss, Local(sim, 1.978, -2.266)).ToDouble(), Is.InRange(-0.01, 0.01), "правая лапа");
            Assert.That(sim.ThicketHullGap(Boss, Local(sim, -2.036, 0)).ToDouble(), Is.InRange(-0.01, 0.01), "хвост");
            // Центр и ось тела закрыты целиком — без щелей между кругами.
            for (double f = -1.9; f <= 2.6; f += 0.05)
                Assert.That(sim.ThicketHullGap(Boss, Local(sim, f, 0)).ToDouble(), Is.LessThan(-0.05), "ось " + f);
            // Герой вплотную к груди стоит на дальности подхода 3,22.
            double chest = 2.737 + sim.Entities.BodyRadius[0].ToDouble();
            Assert.That(chest, Is.LessThanOrEqualTo(Simulation.ThicketHoldDistance.ToDouble()));
        }

        // ---------- проход ----------

        [Test]
        public void Walk_IntoTheBoss_From16Directions_NeverEndsInsideTheHull()
        {
            for (int d = 0; d < 16; d++)
            {
                // Чётные — босс идёт навстречу, нечётные — стоит и только доворачивается.
                var sim = Arena(walks: d % 2 == 0);
                Only(sim);
                Until(sim, IntroDone);
                var e = sim.Entities;
                double angle = 2 * Math.PI * d / 16;
                e.Position[0] = e.Position[Boss] + At(7 * Math.Cos(angle), 7 * Math.Sin(angle));
                double nearest = double.MaxValue;
                int sinceLifted = int.MaxValue;
                for (int k = 0; k < 120; k++)
                {
                    sim.Step(Walk(e.Position[Boss]));
                    // В замахе лапы передние лапы подняты — держат грудь, талия, бёдра, хвост (баланс 02.10, ночь);
                    // опустилась лапа на героя под ней — выдавливает не быстрее 0,4 м за тик (до 4 тиков).
                    sinceLifted = sim.ThicketPawsLifted(Boss) ? 0 : sinceLifted == int.MaxValue ? int.MaxValue : sinceLifted + 1;
                    double gap = HoldingGap(sim, 0);
                    if (sinceLifted > 4) Assert.That(gap, Is.GreaterThanOrEqualTo(-Tolerance), "направление " + d + ", тик " + k);
                    nearest = Math.Min(nearest, Gap(sim, 0));
                }
                Assert.That(nearest, Is.LessThan(0.2), "направление " + d + ": дошёл до корпуса");
            }
        }

        [Test]
        public void Lunge_IntoTheHull_StopsAtItsSurface_EveryTick()
        {
            // Сам вошёл (выпад, волок — по 0,9 м за тик) — снимается целиком, без потолка 0,4.
            var sim = Arena();
            var e = sim.Entities;
            e.Position[0] = AgainstCircle(sim, 0, 0.5);
            var deep = sim.Entities.Position[Boss];
            ForcedMotion.Begin(e, 0, deep, 4, ForcedMotionKind.Lunge);
            for (int k = 0; k < 6; k++)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(Gap(sim, 0), Is.GreaterThanOrEqualTo(-Tolerance), "тик " + k);
            }
            Assert.That(Gap(sim, 0), Is.LessThan(0.05), "встал на поверхности груди");
        }

        [Test]
        public void Turning_InPlace_MovesTheHeroOutSmoothly_NotFlung()
        {
            var sim = Arena();
            Only(sim);
            Until(sim, IntroDone);
            var e = sim.Entities;
            // Вплотную к левой лапе снаружи — босс доворачивает к герою, лапа заходит на него.
            e.Position[0] = AgainstCircle(sim, 1);
            sim.Step(InputFrame.Empty);
            var previous = e.Position[0];
            double worst = 0;
            for (int k = 0; k < 40; k++)
            {
                sim.Step(InputFrame.Empty);
                double moved = FixVec2.Distance(previous, e.Position[0]).ToDouble();
                worst = Math.Max(worst, moved);
                previous = e.Position[0];
                Assert.That(Gap(sim, 0), Is.GreaterThanOrEqualTo(-Tolerance), "тик " + k);
            }
            Assert.That(worst, Is.LessThanOrEqualTo(Simulation.ThicketHullPushPerTick.ToDouble() + Tolerance),
                "за тик не дальше потолка выдавливания");
        }

        [Test]
        public void DiveExit_OnTopOfTheHero_PushesOutAtMostPointFourPerTick()
        {
            // Тело появилось на герое (выход из нырка, появление): выдавливает по 0,4 за тик.
            var sim = Arena();
            var e = sim.Entities;
            e.Position[0] = sim.ThicketHullCircleCenter(Boss, 0);
            var previous = e.Position[0];
            int ticks = 0;
            while (Gap(sim, 0) < -Tolerance && ticks < 20)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(FixVec2.Distance(previous, e.Position[0]).ToDouble(),
                    Is.LessThanOrEqualTo(Simulation.ThicketHullPushPerTick.ToDouble() + Tolerance), "тик " + ticks);
                previous = e.Position[0];
                ticks++;
            }
            Assert.That(ticks, Is.InRange(2, 6), "выход из груди за несколько тиков");
            Assert.That(Gap(sim, 0), Is.GreaterThanOrEqualTo(-Tolerance));
        }

        [Test]
        public void Hold_WalkingBossStopsAtTheHull_DoesNotShoveTheHero_AndStartsThePaw()
        {
            var sim = Arena(distance: 9, walks: true);
            Only(sim);
            // Без топота (и в жребии рядом с лапой, 03.10): его отброс двигает героя законно.
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
            sim.SetThicketStompPickReadyTick(Boss, int.MaxValue / 2);
            var e = sim.Entities;
            var hero = e.Position[0];
            int paws = 0;
            double nearest = double.MaxValue;
            for (int k = 0; k < 700; k++)
            {
                int tick = sim.Tick;
                e.Position[0] = hero;   // стоит на месте; сдвинуть его может только корпус
                sim.Step(InputFrame.Empty);
                Assert.That(FixVec2.Distance(hero, e.Position[0]).ToDouble(), Is.LessThan(Tolerance),
                    "ход босса не толкает героя, тик " + tick);
                nearest = Math.Min(nearest, Gap(sim, 0));
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketPaw
                        && ev.Amount == 0) paws++;
            }
            Assert.That(paws, Is.GreaterThanOrEqualTo(3), "подошёл и бьёт лапой сериями");
            Assert.That(nearest, Is.InRange(0, 0.5), "встал у героя, не в нём");
        }

        [Test]
        public void Hold_HeroAgainstTheChest_BeyondTheHoldDistance_BossDoesNotStepIntoHim()
        {
            // 0,2 м до груди — 3,39 м между центрами, дальше подхода 3,22: по центрам он бы шагнул и толкнул.
            var sim = Arena(walks: true);
            Only(sim);
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
            sim.SetThicketStompPickReadyTick(Boss, int.MaxValue / 2);
            Until(sim, IntroDone);
            var e = sim.Entities;
            var hero = Local(sim, 2.737 + e.BodyRadius[0].ToDouble() + 0.2, 0);
            Assert.That(FixVec2.Distance(hero, e.Position[Boss]).ToDouble(), Is.GreaterThan(Simulation.ThicketHoldDistance.ToDouble()));
            var boss = e.Position[Boss];
            for (int k = 0; k < 300; k++)
            {
                e.Position[0] = hero;
                sim.Step(InputFrame.Empty);
                Assert.That(FixVec2.Distance(boss, e.Position[Boss]).ToDouble(), Is.LessThan(Tolerance), "босс не шагает, тик " + k);
                Assert.That(FixVec2.Distance(hero, e.Position[0]).ToDouble(), Is.LessThan(Tolerance), "героя не толкает, тик " + k);
            }
        }

        [Test]
        public void Paw_StartsFromThePawCorner_BeyondTheOldCentreRange()
        {
            var sim = Arena();
            Only(sim);
            Until(sim, IntroDone);
            var e = sim.Entities;
            e.Position[0] = AgainstCircle(sim, 1);
            double distance = FixVec2.Distance(e.Position[0], e.Position[Boss]).ToDouble();
            Assert.That(distance, Is.GreaterThan(Simulation.ThicketPawStartRange.ToDouble()), "по центрам лапа бы не достала");
            bool started = false;
            for (int k = 0; k < 10 && !started; k++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketPaw
                        && ev.Amount == 0) started = true;
            }
            Assert.IsTrue(started, "герой вплотную к лапе — лапа бьёт");
        }

        // ---------- рывок ----------

        [TestCase(1.978, TestName = "Dash_AcrossTheBoss_LandsOnTheFarSide(лапы)")]
        [TestCase(0.9, TestName = "Dash_AcrossTheBoss_LandsOnTheFarSide(талия)")]
        [TestCase(-0.52, TestName = "Dash_AcrossTheBoss_LandsOnTheFarSide(бёдра)")]
        public void Dash_AcrossTheBoss_LandsOnTheFarSide(double forward)
        {
            // Босс спит (до 90-го тика) и не доворачивается — поперёк тела ровно.
            var sim = Arena();
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            var e = sim.Entities;
            // Справа вплотную к корпусу.
            double side = -0.5;
            while (sim.ThicketHullGap(Boss, Local(sim, forward, side)).ToDouble() < e.BodyRadius[0].ToDouble() + 0.01) side -= 0.01;
            e.Position[0] = Local(sim, forward, side);
            var left = Left(sim);
            sim.Step(Dash(Local(sim, forward, 10)));
            bool wasInside = false;
            for (int k = 0; k < 20; k++)
            {
                sim.Step(InputFrame.Empty);
                if (Gap(sim, 0) < -0.1) wasInside = true;
            }
            Assert.IsTrue(wasInside, "рывок шёл сквозь корпус");
            Assert.IsFalse(sim.PelagDash.CutShort, "тело рывок не сорвало");
            Assert.That(Gap(sim, 0), Is.GreaterThanOrEqualTo(-Tolerance), "снаружи");
            Assert.That(FixVec2.Dot(e.Position[0] - e.Position[Boss], left).ToDouble(), Is.GreaterThan(0), "на той стороне");
            Assert.IsFalse(sim.ThicketMasterAwake(Boss));
        }

        [Test]
        public void Dash_EndingDeepInside_LeavesByTheNearestSide()
        {
            // Рывок в грудь издали кончился внутри головы, насквозь — дальше 2,5 м: назад, к ближнему краю.
            var sim = Arena();
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            var e = sim.Entities;
            e.Position[0] = Local(sim, 6.6, 0);
            sim.Step(Dash(sim.Entities.Position[Boss]));
            for (int k = 0; k < 20; k++) sim.Step(InputFrame.Empty);
            Assert.That(Gap(sim, 0), Is.GreaterThanOrEqualTo(-Tolerance));
            Assert.That(FixVec2.Dot(e.Position[0] - e.Position[Boss], Forward(sim)).ToDouble(), Is.GreaterThan(2.5),
                "вышел перед грудью, не через весь корпус");
        }

        // ---------- нырок ----------

        [Test]
        public void Burrowed_BossBlocksNothing_HeroWalksThroughItsBody()
        {
            var sim = Arena(distance: 9);
            Only(sim, ThicketMasterAction.Dive);
            Until(sim, IntroDone);
            var e = sim.Entities;
            // Нырок — по герою в дальней полосе (ревью 02.10, ночь): 10 м перед мордой.
            e.Position[0] = e.Position[Boss] + At(-10, 0);
            bool diving = false;
            for (int k = 0; k < 400 && !diving; k++)
            {
                sim.Step(InputFrame.Empty);
                diving = sim.ThicketShielded(Boss);
            }
            Assert.IsTrue(diving, "нырок начался");
            Assert.IsFalse(sim.ThicketHullActive(Boss));
            Assert.IsFalse(sim.TryGetThicketHullCircle(Boss, 0, out _, out _), "в нырке корпуса нет");
            for (int k = 0; k < Simulation.ThicketHullCircleCount; k++)
            {
                var spot = sim.ThicketHullCircleCenter(Boss, k);
                e.Position[0] = spot;
                sim.Step(InputFrame.Empty);
                Assert.IsTrue(sim.ThicketShielded(Boss));
                Assert.AreEqual(spot, e.Position[0], "круг " + k + " в нырке не держит");
            }
        }

        // ---------- попадания героя ----------

        private static Simulation HitArena()
        {
            var sim = Arena();
            var e = sim.Entities;
            e.Stats[0].SetBase(StatType.Damage, Fix64.FromInt(40));
            e.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            sim.RefreshPlayerStats(false);
            return sim;
        }

        private static int Swing(Simulation sim, int ticks)
        {
            int dealt = 0;
            for (int k = 0; k < ticks; k++)
            {
                var input = InputFrame.Empty;
                input.Aim = sim.Entities.Position[Boss];
                input.Flags = (byte)(k == 0 ? InputFlags.AttackPressed : InputFlags.Attack);
                sim.Step(input);
                dealt += BossDamage(sim, DamageOrigin.BasicAttack);
            }
            return dealt;
        }

        [Test]
        public void Sabre_FromThePawCorner_Hits_BeyondTheCentreReach()
        {
            var sim = HitArena();
            var e = sim.Entities;
            e.Position[0] = AgainstCircle(sim, 2);
            double distance = FixVec2.Distance(e.Position[0], e.Position[Boss]).ToDouble();
            Assert.That(distance, Is.GreaterThan((Simulation.SabreReach + e.BodyRadius[Boss]).ToDouble()),
                "по центру и телу 0,95 сабля бы не достала");
            Assert.That(Swing(sim, 20), Is.GreaterThan(0), "сабля от лапы попадает");
            Assert.AreEqual(EnemyArchetypes.ThicketMasterBodyRadius, e.BodyRadius[Boss], "тело после окна ударов — прежнее");
        }

        [Test]
        public void Sabre_FromTheRumpAndTheSide_Hits()
        {
            var rump = HitArena();
            rump.Entities.Position[0] = AgainstCircle(rump, 6);
            Assert.That(Swing(rump, 20), Is.GreaterThan(0), "от крупа");

            var side = HitArena();
            side.Entities.Position[0] = AgainstCircle(side, 5);
            Assert.That(Swing(side, 20), Is.GreaterThan(0), "от бедра сбоку");

            var head = HitArena();
            head.Entities.Position[0] = AgainstCircle(head, 0);
            Assert.That(Swing(head, 20), Is.GreaterThan(0), "от головы");
        }

        [Test]
        public void BasicCombo_FromTheChest_Hits_BeyondTheCentreReach()
        {
            var sim = HitArena();
            sim.EnablePelagBasicCombo();
            var e = sim.Entities;
            e.Position[0] = AgainstCircle(sim, 0);
            double distance = FixVec2.Distance(e.Position[0], e.Position[Boss]).ToDouble();
            Assert.That(distance, Is.GreaterThan((Simulation.PelagBasicAttackRange + e.BodyRadius[Boss]).ToDouble()),
                "по центру и телу 0,95 удар бы не достал");
            Assert.That(Swing(sim, 20), Is.GreaterThan(0), "удар от головы попадает");
        }

        [Test]
        public void Whirlwind_FromBehindTheRump_Hits_ThoughTheCentreIsOutOfItsRadius()
        {
            var sim = HitArena();
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            var e = sim.Entities;
            e.Lavidium[0] = Fix64.FromInt(e.MaxLavidium[0]);
            e.Position[0] = AgainstCircle(sim, 6);
            double distance = FixVec2.Distance(e.Position[0], e.Position[Boss]).ToDouble();
            Assert.That(distance, Is.GreaterThan(2.3), "центр босса вне круга Вихря");
            var cast = InputFrame.Empty;
            cast.AbilityMask = 1;
            cast.Aim = e.Position[Boss];
            sim.Step(cast);
            int dealt = BossDamage(sim, DamageOrigin.Ability);
            for (int k = 0; k < 20; k++)
            {
                sim.Step(InputFrame.Empty);
                dealt += BossDamage(sim, DamageOrigin.Ability);
            }
            Assert.That(dealt, Is.GreaterThan(0), "Вихрь у крупа задевает корпус");
        }

        [Test]
        public void Anchor_TargetsTheBossByItsHull_FliesToTheChest_AndThePunchLands()
        {
            var sim = HitArena();
            sim.SetAbility(0, AbilityDefinition.AnchorLeap(), new AbilityNode[0], 0);
            var build = sim.GetAbility(0);
            var e = sim.Entities;
            e.Lavidium[0] = Fix64.FromInt(e.MaxLavidium[0]);
            // 8,5 м до центра — дальше цепи (7), но до груди 5,8.
            e.Position[0] = Local(sim, 8.5, 0);
            Assert.That(FixVec2.Distance(e.Position[0], e.Position[Boss]).ToDouble(), Is.GreaterThan(build.Get(AbilityStatType.Radius).ToDouble()));
            Assert.IsTrue(sim.ValidAbilityTarget(Boss, build), "цель — по корпусу");
            e.Position[0] = Local(sim, 10.5, 0);
            Assert.IsFalse(sim.ValidAbilityTarget(Boss, build), "до груди дальше цепи — не цель");

            e.Position[0] = Local(sim, 8.5, 0);
            var cast = InputFrame.Empty;
            cast.AbilityMask = 1;
            cast.AbilityTarget = Boss;
            cast.Aim = e.Position[Boss];
            sim.Step(cast);
            int dealt = BossDamage(sim, DamageOrigin.Ability);
            for (int k = 0; k < 45; k++)
            {
                sim.Step(InputFrame.Empty);
                dealt += BossDamage(sim, DamageOrigin.Ability);
                Assert.That(Gap(sim, 0), Is.GreaterThanOrEqualTo(-Tolerance), "в полёте не входит в корпус, тик " + k);
            }
            Assert.That(dealt, Is.GreaterThan(0), "кулак Абордажа у груди попадает");
            Assert.That(Gap(sim, 0), Is.LessThan(0.3), "долетел до груди");
        }

        [Test]
        public void Burrowed_HitBodyIsNotWidened_NoDamage()
        {
            var sim = HitArena();
            Only(sim, ThicketMasterAction.Dive);
            Until(sim, IntroDone);
            var e = sim.Entities;
            e.Position[0] = e.Position[Boss] + At(-10, 0);
            bool diving = false;
            for (int k = 0; k < 400 && !diving; k++)
            {
                sim.Step(InputFrame.Empty);
                diving = sim.ThicketShielded(Boss);
            }
            Assert.IsTrue(diving);
            e.Position[0] = e.Position[Boss] + At(-1.5, 0);
            Assert.AreEqual(0, Swing(sim, 8), "под землёй не бьётся");
        }

        // ---------- тяга и выпад в корпус: встаёт там, где вошёл ----------

        private static void AssertInFrontOfTheChest(Simulation sim, string where)
        {
            var offset = sim.Entities.Position[0] - sim.Entities.Position[Boss];
            Assert.That(Gap(sim, 0), Is.GreaterThanOrEqualTo(-Tolerance), "снаружи корпуса, " + where);
            Assert.That(FixVec2.Dot(offset, Forward(sim)).ToDouble(), Is.GreaterThan(2.5), "перед грудью, " + where);
            Assert.That(Math.Abs(FixVec2.Dot(offset, Left(sim)).ToDouble()), Is.LessThan(0.5), "не на боку, " + where);
        }

        [TestCase(4.5)]
        [TestCase(5.0)]
        [TestCase(6.0)]
        [TestCase(7.5)]
        public void Anchor_FromAnyRange_StopsAtTheChest_NoSidewaysJump(double distance)
        {
            // Тяга якоря целит в 0,9 м от центра, последний её тик берёт весь остаток пути. Из
            // глубины тела ближайшая точка снаружи — выемка у бока: был телепорт на 1,55 м вбок.
            // Абордаж (сессия Пелага, 03.10): тяга 20 м/с (~0,67 м за тик), посадка с зазором 0,1 м
            // от тела — порог шага 0,85, «долетел» до 0,15. Посадку вплотную проверяет AbordageTests.
            var sim = HitArena();
            sim.SetAbility(0, AbilityDefinition.AnchorLeap(), new AbilityNode[0], 0);
            var e = sim.Entities;
            e.Lavidium[0] = Fix64.FromInt(e.MaxLavidium[0]);
            e.Position[0] = Local(sim, distance, 0);
            var cast = InputFrame.Empty;
            cast.AbilityMask = 1;
            cast.AbilityTarget = Boss;
            cast.Aim = e.Position[Boss];
            sim.Step(cast);
            var previous = e.Position[0];
            double nearest = double.MaxValue;
            for (int k = 0; k < 45; k++)
            {
                sim.Step(InputFrame.Empty);
                AssertInFrontOfTheChest(sim, "тик " + k);
                Assert.That(FixVec2.Distance(previous, e.Position[0]).ToDouble(), Is.LessThan(0.85), "без скачка, тик " + k);
                previous = e.Position[0];
                nearest = Math.Min(nearest, Gap(sim, 0));
            }
            Assert.That(nearest, Is.LessThan(0.15), "долетел до груди");
        }

        [TestCase(3.6)]
        [TestCase(5.0)]
        public void Skewer_IntoTheChest_StopsInFront_DoesNotPassThrough_AndHits(double distance)
        {
            // Шквал на 6 м в грудь: прежде герой выходил за хвостом (без рывка — сквозь тело).
            var sim = HitArena();
            sim.SetAbility(0, AbilityDefinition.Skewer(), new AbilityNode[0], 0);
            var e = sim.Entities;
            e.Lavidium[0] = Fix64.FromInt(e.MaxLavidium[0]);
            e.Position[0] = Local(sim, distance, 0);
            var cast = InputFrame.Empty;
            cast.AbilityMask = 1;
            cast.Aim = e.Position[Boss];
            sim.Step(cast);
            int dealt = BossDamage(sim, DamageOrigin.Ability);
            double nearest = double.MaxValue;
            for (int k = 0; k < 20; k++)
            {
                sim.Step(InputFrame.Empty);
                dealt += BossDamage(sim, DamageOrigin.Ability);
                AssertInFrontOfTheChest(sim, "тик " + k);
                nearest = Math.Min(nearest, Gap(sim, 0));
            }
            Assert.That(nearest, Is.LessThan(0.05), "упёрся в грудь");
            Assert.That(dealt, Is.GreaterThan(0), "Шквал бьёт грудь, в которую упёрся");
        }

        [Test]
        public void Lunge_AcrossTheBodyInOneTick_StopsWhereItEntered()
        {
            // Выпад поперёк лапы и талии — 3,5 м за тик: насквозь не проходит и не выходит
            // у ближнего края из глубины, а встаёт там, где вошёл, — у правой лапы.
            var sim = Arena();
            var e = sim.Entities;
            double side = -3.0;
            e.Position[0] = Local(sim, 1.0, side);
            var far = Local(sim, 1.0, 4.0);
            ForcedMotion.Begin(e, 0, far, 2, ForcedMotionKind.Lunge);
            sim.Step(InputFrame.Empty);
            var offset = e.Position[0] - e.Position[Boss];
            Assert.That(FixVec2.Dot(offset, Left(sim)).ToDouble(), Is.LessThan(-1.0), "не прошёл талию насквозь");
            Assert.That(Gap(sim, 0), Is.InRange(-Tolerance, 0.05), "встал у корпуса");
        }

        [TestCase(1.978, TestName = "Dash_EndingInside_LeavesWithinTwoTicks(лапы)")]
        [TestCase(0.9, TestName = "Dash_EndingInside_LeavesWithinTwoTicks(талия)")]
        [TestCase(-0.52, TestName = "Dash_EndingInside_LeavesWithinTwoTicks(бёдра)")]
        public void Dash_EndingInside_LeavesWithinTwoTicks(double forward)
        {
            // Окно неуязвимости рывка кончилось — сквозь остаток тела не ползёт по 0,4 за тик.
            var sim = Arena();
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            var e = sim.Entities;
            double side = -0.5;
            while (sim.ThicketHullGap(Boss, Local(sim, forward, side)).ToDouble() < e.BodyRadius[0].ToDouble() + 0.01) side -= 0.01;
            e.Position[0] = Local(sim, forward, side);
            sim.Step(Dash(Local(sim, forward, 10)));
            int vulnerableInside = 0;
            for (int k = 0; k < 20; k++)
            {
                bool vulnerable = !sim.DashInvulnerable;   // на тике, который сейчас пройдёт
                sim.Step(InputFrame.Empty);
                if (vulnerable && Gap(sim, 0) < -Tolerance) vulnerableInside++;
            }
            Assert.That(vulnerableInside, Is.LessThanOrEqualTo(1), "после окна рывка внутри не дольше тика");
            Assert.That(FixVec2.Dot(e.Position[0] - e.Position[Boss], Left(sim)).ToDouble(), Is.GreaterThan(0), "на той стороне");
        }

        // ---------- площади и фигуры ударов — по корпусу, не от героя ----------

        [Test]
        public void ThicketBodyFrom_MeasuresTheHullFromTheAreaCentre_WhereverTheHeroIs()
        {
            var sim = Arena();
            var e = sim.Entities;
            bool Touches(FixVec2 at, double radius)
            {
                Fix64 reach = Fix64.FromDouble(radius) + sim.ThicketBodyFrom(Boss, at);
                return FixVec2.DistanceSq(at, e.Position[Boss]) <= reach * reach;
            }
            foreach (double heroForward in new[] { 6.0, -6.0 })
            {
                e.Position[0] = Local(sim, heroForward, 0);
                string hero = heroForward > 0 ? "герой спереди" : "герой сзади";
                // Лужа радиусом 0,5: хвост до 2,036 м назад, лапа до 2,266 м вбок.
                Assert.IsFalse(Touches(Local(sim, -2.036 - 0.61, 0), 0.5), "за хвостом в 0,11 м — мимо, " + hero);
                Assert.IsTrue(Touches(Local(sim, -2.036 - 0.4, 0), 0.5), "хвост в луже, " + hero);
                Assert.IsTrue(Touches(Local(sim, 1.978, 2.266 + 0.3), 0.5), "лапа в луже, " + hero);
                Assert.IsFalse(Touches(Local(sim, 1.978, 2.266 + 0.7), 0.5), "у лапы в 0,2 м — мимо, " + hero);
            }
            // Не босс — его обычное тело.
            int add = sim.SpawnEnemy(Local(sim, 0, 8), 1000, EnemyKind.ForestRootSwarm);
            Assert.AreEqual(e.BodyRadius[add], sim.ThicketBodyFrom(add, Local(sim, 0, 9)));
        }

        [Test]
        public void ThicketHitTouches_Sector_AtThePaw_Hits_AwayFromTheBody_Misses()
        {
            var sim = Arena();
            var e = sim.Entities;
            var hero = AgainstCircle(sim, 1);
            var paw = sim.ThicketHullCircleCenter(Boss, 1);
            var toPaw = (paw - hero).Normalized();
            var at = EnemyTelegraph.Sector(hero, toPaw, Simulation.SabreReach, Simulation.SabreArcCos);
            Assert.IsTrue(sim.ThicketHitTouches(in at, Boss), "к лапе — задевает");
            var away = EnemyTelegraph.Sector(hero, toPaw * -Fix64.One, Simulation.SabreReach, Simulation.SabreArcCos);
            Assert.IsFalse(sim.ThicketHitTouches(in away, Boss), "прочь от тела — мимо");
            // Не босс — как TelegraphContains по BodyRadius.
            int add = sim.SpawnEnemy(hero + toPaw * -Fix64.FromInt(2), 1000, EnemyKind.ForestRootSwarm);
            Assert.AreEqual(Simulation.TelegraphContains(in away, e.Position[add], e.BodyRadius[add]),
                sim.ThicketHitTouches(in away, add));
            Assert.IsTrue(sim.ThicketHitTouches(in away, add));
        }

        [Test]
        public void ThicketHullInLane_TouchesOnlyTheRealBody()
        {
            var sim = Arena();
            var forward = Forward(sim);
            var back = forward * -Fix64.One;
            Fix64 half = Fix64.FromDouble(0.5), length = Fix64.FromInt(6);
            Fix64 along = Fix64.FromInt(10);
            // Полоса от груди назад вдоль оси — тело.
            Assert.IsTrue(sim.ThicketHullInLane(Boss, Local(sim, 4, 0), back, length, half));
            // Вдоль бока (лапа до 2,266 м вбок): край полосы в 0,11 м от лапы — мимо, на 0,1 м в ней — задевает.
            Assert.IsFalse(sim.ThicketHullInLane(Boss, Local(sim, 5, 2.266 + 0.61), back, along, half));
            Assert.IsTrue(sim.ThicketHullInLane(Boss, Local(sim, 5, 2.266 + 0.4), back, along, half));
            // Полоса, направленная прочь, — мимо.
            Assert.IsFalse(sim.ThicketHullInLane(Boss, Local(sim, 4, 0), forward, length, half));
        }

        // ---------- подмога ----------

        [Test]
        public void AddWave_DoesNotSpawnInsideTheHull()
        {
            // Волна на 66% разносится от тел по WaveSpotFree: у босса — по корпусу, не по 0,95.
            var location = ArenaEncounterTests.ForestLocation();
            FixVec2 taken = default;
            for (int pass = 0; pass < 2; pass++)
            {
                var map = ArenaEncounterTests.ArenaMap(location, 9, 5);
                var sim = new Simulation(5, 512) { ThicketMasterBossEnabled = true };
                var plan = location.GetLevel(9).Spawn(sim, map, 5, null, 9);
                sim.PlayerInvulnerable = true;
                int boss = plan.BossId;
                var e = sim.Entities;
                if (pass == 1)
                {
                    // Грудь босса — на месте первого из подмоги в прошлом прогоне: центр в 1,9 м,
                    // дальше прежнего разноса 0,95 + тело + 0,1, но точка — в самом корпусе.
                    var facing = e.Facing[boss].Normalized();
                    e.Position[boss] = taken - facing * Fix64.FromDouble(1.898);
                    Assert.That(sim.ThicketHullGap(boss, taken).ToDouble(), Is.LessThan(-0.5), "точка в груди");
                }
                int before = e.Count;
                e.Health[boss] = e.MaxHealth[boss] * 66 / 100;
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(1, sim.BossAddWavesSpawned, "волна на 66%");
                Assert.That(e.Count, Is.GreaterThan(before), "подмога вышла");
                if (pass == 0) { taken = e.Position[before]; continue; }
                for (int id = before; id < e.Count; id++)
                    Assert.That((sim.ThicketHullGap(boss, e.Position[id]) - e.BodyRadius[id]).ToDouble(),
                        Is.GreaterThanOrEqualTo(0.1 - Tolerance), "моб " + id + " встал не в корпусе");
            }
        }

        [Test]
        public void Adds_AreKeptOutOfTheHull()
        {
            var sim = Arena();
            var e = sim.Entities;
            // Подмога встала внутри корпуса (грудь, талия, бедро, хвост) и перед мордой; герой — за спиной.
            var adds = new List<int>();
            foreach (int k in new[] { 0, 3, 4, 6 })
                adds.Add(sim.SpawnEnemy(sim.ThicketHullCircleCenter(Boss, k), 10000, EnemyKind.ForestRootSwarm));
            adds.Add(sim.SpawnEnemy(Local(sim, 5, 0.5), 10000, EnemyKind.ForestGuardian));
            adds.Add(sim.SpawnEnemy(Local(sim, 5, -0.5), 10000, EnemyKind.ForestRootSwarm));
            e.Position[0] = Local(sim, -6, 0);
            foreach (int id in adds) e.Aggro[id] = true;
            for (int k = 0; k < 200; k++)
            {
                sim.Step(InputFrame.Empty);
                if (k < 6) continue;   // встали внутри — выдавливает по 0,4 за тик
                foreach (int id in adds)
                    if (e.Alive[id]) Assert.That(Gap(sim, id), Is.GreaterThanOrEqualTo(-Tolerance), "моб " + id + ", тик " + k);
            }
        }

        // ---------- детерминизм ----------

        [Test]
        public void TwoRuns_SameHashAndEventsEveryTick()
        {
            var a = new Run();
            var b = new Run();
            for (int t = 0; t < 700; t++)
            {
                ulong ea = a.Step(t), eb = b.Step(t);
                Assert.That(b.Sim.StateHash(), Is.EqualTo(a.Sim.StateHash()), "StateHash разошёлся на тике " + t);
                Assert.That(eb, Is.EqualTo(ea), "события разошлись на тике " + t);
            }
            Assert.That(a.Pushed, Is.GreaterThan(0), "корпус ни разу не держал героя");
            Assert.That(a.Hits, Is.GreaterThan(0), "ни одного попадания по боссу");
        }

        private sealed class Run
        {
            public readonly Simulation Sim;
            public int Pushed, Hits;

            public Run()
            {
                Sim = Arena(walks: true);
                Sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
                Sim.SpawnEnemy(Sim.Entities.Position[Boss] + At(0, 4), 10000, EnemyKind.ForestRootSwarm);
                Sim.SpawnEnemy(Sim.Entities.Position[Boss] + At(1, -4), 10000, EnemyKind.ForestRootSwarm);
            }

            public ulong Step(int t)
            {
                var e = Sim.Entities;
                var boss = e.Position[Boss];
                var input = InputFrame.Empty;
                int phase = t / 60 % 6;
                if (phase == 0 || phase == 2) input = Walk(boss);                                // в корпус
                else if (phase == 1) input = Walk(boss + At(Math.Cos(t * 0.05) * 4, Math.Sin(t * 0.05) * 4));  // вокруг
                else if (phase == 3 && t % 60 == 0) input = Dash(boss + (boss - e.Position[0]));  // насквозь
                else if (phase >= 4)
                {
                    input.Aim = boss;
                    input.Flags = (byte)InputFlags.Attack;
                }
                var before = e.Position[0];
                Sim.Step(input);
                ulong hash = 1469598103934665603UL;
                foreach (var ev in Sim.Events)
                {
                    Mix(ref hash, (ulong)ev.Type); Mix(ref hash, (ulong)(uint)ev.Source); Mix(ref hash, (ulong)(uint)ev.Target);
                    Mix(ref hash, (ulong)(uint)ev.Amount); Mix(ref hash, ev.Flag ? 1UL : 0UL);
                    Mix(ref hash, (ulong)ev.Position.X.Raw); Mix(ref hash, (ulong)ev.Position.Y.Raw);
                    if (ev.Type == SimEventType.Damage && ev.Target == Boss) Hits++;
                }
                if (input.Flags == (byte)InputFlags.MoveOrder && Gap(Sim, 0) < 0.01) Pushed++;
                return hash;
            }

            private static void Mix(ref ulong hash, ulong value)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }
    }
}
