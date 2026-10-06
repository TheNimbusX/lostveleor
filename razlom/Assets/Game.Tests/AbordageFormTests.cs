using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AbordageTests;

namespace Game.Tests
{
    /// <summary>
    /// Формы Абордажа (SPEC 3, тест 9): Обвал, Гейзер, Пробоина; таланты линии на
    /// новых сроках (3.1), заготовка Метки, таблица форм и детерминизм с формами.
    /// </summary>
    public sealed class AbordageFormTests
    {
        private static int DamageTo(List<Frame> frames, int target, out List<int> ticks)
        {
            int total = 0;
            ticks = new List<int>();
            foreach (var (frame, ev) in Of(frames, SimEventType.Damage))
                if (ev.Target == target) { total += ev.Amount; ticks.Add(frame.Tick); }
            return total;
        }

        // ---------- таблица ----------

        [Test]
        public void Forms_AreQuakeGeyserBreach_ReadyOnTheAbordageLine()
        {
            Assert.AreEqual(8, (int)PelagForm.AbordageQuake);
            Assert.AreEqual(9, (int)PelagForm.AbordageGeyser);
            Assert.AreEqual(10, (int)PelagForm.AbordageBreach);
            int line = PelagKit.PoolIndexOf(AbilityDefinition.AnchorLeapId);
            Assert.AreEqual(3, PelagForms.ReadyFormCount(line));
            Assert.AreEqual(PelagForm.AbordageQuake, PelagForms.ReadyFormAt(line, 0));
            Assert.AreEqual(PelagForm.AbordageGeyser, PelagForms.ReadyFormAt(line, 1));
            Assert.AreEqual(PelagForm.AbordageBreach, PelagForms.ReadyFormAt(line, 2));
            Assert.AreEqual("form.abordage.quake", PelagForms.KeyOf(PelagForm.AbordageQuake));
            Assert.AreEqual("form.abordage.geyser", PelagForms.KeyOf(PelagForm.AbordageGeyser));
            Assert.AreEqual("form.abordage.breach", PelagForms.KeyOf(PelagForm.AbordageBreach));
            Assert.IsTrue(Arena(PelagForm.AbordageGeyser).FormIs(Slot, PelagForm.AbordageGeyser));
        }

        // ---------- Обвал ----------

        [Test]
        public void Quake_EveryoneInThreeMetresPlusBody_HitOnceWhenTheFrontArrives_FistTargetOnlyFist()
        {
            Simulation sim = Arena(PelagForm.AbordageQuake);
            int target = Enemy(sim, 5000, 0);                       // посадка (3,6; 0)
            int near = Enemy(sim, 3600, -1400, radiusMm: 450);       // 1,4 от центра
            int edge = Enemy(sim, 3600, 3300, radiusMm: 450);        // 3,3: край тела 2,85 — в волне
            int outside = Enemy(sim, 1200, 1800, radiusMm: 450);     // 3,0: край 2,55 — в волне
            int far = Enemy(sim, 3600, -3600, radiusMm: 450);        // 3,6: край 3,15 — вне
            sim.MarkElite(edge);
            List<Frame> frames = Cast(sim, target, 30);
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            var quake = Of(frames, SimEventType.AbordageQuake)[0];
            Assert.AreEqual(strike, quake.frame.Tick);
            Assert.AreEqual(Simulation.AbordageQuakeTravelTicks, quake.ev.Amount);
            Assert.AreEqual(300, quake.ev.ActionVariant);

            Assert.AreEqual(75, DamageTo(frames, target, out _), "цель кулака — только кулак");
            Assert.IsTrue(sim.Statuses.IsStunned(target, strike + 20), "и сбита");
            AssertWaveHit(frames, near, strike, 1.4 - 0.45);
            AssertWaveHit(frames, edge, strike, 3.3 - 0.45);
            AssertWaveHit(frames, outside, strike, 3.0 - 0.45);
            Assert.AreEqual(0, DamageTo(frames, far, out _), "вне 3 м + тело");
            Assert.AreEqual(0, Of(frames, SimEventType.AbordageEnded)[0].ev.Amount, "доигран");
        }

        private static void AssertWaveHit(List<Frame> frames, int id, int strike, double edge)
        {
            Assert.AreEqual(38, DamageTo(frames, id, out List<int> ticks), "волна — 50 % кулака, один раз");
            int expected = strike + (int)System.Math.Ceiling(edge / 0.6 - 1e-9) - 1;
            Assert.AreEqual(expected, ticks[0], "в тик прихода фронта, край " + edge);
        }

        [Test]
        public void Quake_KnocksDownAndShovesTheLight_EliteStands_BossIsNotKnockedDown()
        {
            Simulation sim = Arena(PelagForm.AbordageQuake);
            int target = Enemy(sim, 5000, 0);
            int light = Enemy(sim, 3600, -1400, radiusMm: 450);
            int elite = Enemy(sim, 3600, 1400, radiusMm: 450);
            sim.MarkElite(elite);
            List<Frame> frames = Cast(sim, target, 19);
            Assert.AreEqual(11, TickOf(frames, SimEventType.AbordagePunch));
            Assert.IsTrue(sim.Statuses.IsStunned(light, sim.Tick), "сбит");
            Assert.IsTrue(sim.Statuses.IsStunned(elite, sim.Tick), "элита тоже сбита");
            Assert.Less(sim.Entities.Position[light].Y.ToDouble(), -1.45, "лёгкого толкнуло наружу");
            Assert.AreEqual(1.4, sim.Entities.Position[elite].Y.ToDouble(), 1e-3, "элита стоит");
        }

        // ---------- Гейзер ----------

        [Test]
        public void Geyser_LightTargetFlies24Ticks_Stunned_StaysPut_TakesDamage_ThenTheWaterFalls()
        {
            Simulation sim = Arena(PelagForm.AbordageGeyser);
            int target = Enemy(sim, 5000, 0, radiusMm: 450);
            int splash = Enemy(sim, 6400, 1200, radiusMm: 450);    // 1,84 от цели — в 2 м + тело
            int dry = Enemy(sim, 8200, 0, radiusMm: 450);          // 3,2 — вне
            List<Frame> frames = Cast(sim, target, 13);
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            var lift = Of(frames, SimEventType.AbordageGeyserLift)[0];
            Assert.AreEqual(strike, lift.frame.Tick);
            Assert.IsTrue(lift.ev.Flag, "подброшена");
            Assert.AreEqual(Simulation.AbordageGeyserLiftTicks, lift.ev.Amount);
            FixVec2 at = sim.Entities.Position[target];
            int before = sim.Entities.Health[target];
            sim.ApplyAbilityDamage(0, target, 10, Slot, DamageType.Physical);
            Assert.Less(sim.Entities.Health[target], before, "урон по подброшенной проходит");

            int fall = strike + Simulation.AbordageGeyserLiftTicks;
            List<Frame> rest = Run(sim, 30, t => InputFrame.Empty,
                before: t => Assert.AreEqual(sim.Tick <= fall, sim.AbordageLifted(target), "в воздухе до падения, тик " + sim.Tick));
            foreach (Frame f in rest)
            {
                if (f.Tick >= fall) break;
                Assert.IsTrue(sim.Statuses.IsStunned(target, f.Tick), "в воздухе оглушена, тик " + f.Tick);
            }
            Assert.That(Dist(sim.Entities.Position[target], at), Is.LessThan(1e-6), "не двигается");
            var fallen = Of(rest, SimEventType.AbordageGeyserFall)[0];
            Assert.AreEqual(fall, fallen.frame.Tick);
            Assert.AreEqual(200, fallen.ev.ActionVariant);
            Assert.AreEqual(38, DamageTo(rest, target, out _), "падение бьёт и её");
            Assert.AreEqual(38, DamageTo(rest, splash, out _));
            Assert.AreEqual(0, DamageTo(rest, dry, out _));
            Assert.IsFalse(sim.AbordageLifted(target));
        }

        [Test]
        public void Geyser_HeavyEliteDoesNotFly_StillStunned_AndTheWaterStillFalls()
        {
            Simulation sim = Arena(PelagForm.AbordageGeyser);
            int target = Enemy(sim, 5000, 0);
            sim.MarkElite(target);
            List<Frame> frames = Cast(sim, target, 40, before: t => Assert.IsFalse(sim.AbordageLifted(target)));
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            var lift = Of(frames, SimEventType.AbordageGeyserLift)[0];
            Assert.IsFalse(lift.ev.Flag, "элита не взлетает");
            Assert.IsTrue(sim.Statuses.IsStunned(target, strike + 20), "столб и оглушение");
            Assert.AreEqual(strike + Simulation.AbordageGeyserLiftTicks, TickOf(frames, SimEventType.AbordageGeyserFall));
            Assert.AreEqual(75 + 38, DamageTo(frames, target, out _), "кулак и падение воды");
        }

        // ---------- Пробоина ----------

        [Test]
        public void Breach_ConeBehindTheTarget_HitsEachOnce_KnocksTheLightBack_TargetOnlyFist()
        {
            Simulation sim = Arena(PelagForm.AbordageBreach);
            int target = Enemy(sim, 5000, 0, radiusMm: 450);          // вершина (5,45; 0)
            int behind = Enemy(sim, 7500, 0, radiusMm: 450);          // 2,05 по оси
            int slant = Enemy(sim, 8450, 900, radiusMm: 450);         // 3,13 м, 16,7°
            int wide = Enemy(sim, 7000, 2000, radiusMm: 450);         // 52° — вне конуса
            int beyond = Enemy(sim, 10500, 0, radiusMm: 450);         // 5,05: край 4,6 — вне
            List<Frame> frames = Cast(sim, target, 20);
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            var breach = Of(frames, SimEventType.AbordageBreach)[0];
            Assert.AreEqual(strike, breach.frame.Tick);
            Assert.AreEqual(400, breach.ev.ActionVariant);
            Assert.AreEqual(5.45, breach.ev.Position.X.ToDouble(), 0.01, "вершина — за телом цели");

            Assert.AreEqual(75, DamageTo(frames, target, out _), "цель — только кулак");
            Assert.AreEqual(5.0, sim.Entities.Position[target].X.ToDouble(), 1e-3, "цель стоит");
            Assert.AreEqual(38, DamageTo(frames, behind, out List<int> t1));
            Assert.AreEqual(strike + 1, t1[0], "фронт 1 м/тик: край 1,6 — второй шаг");
            Assert.AreEqual(38, DamageTo(frames, slant, out _));
            Assert.AreEqual(0, DamageTo(frames, wide, out _));
            Assert.AreEqual(0, DamageTo(frames, beyond, out _));
            Assert.Greater(sim.Entities.Position[behind].X.ToDouble(), 8.9, "отброшен на 1,5 м от вершины");
        }

        // ---------- босс ----------

        [Test]
        public void Boss_IsHookedByTheHull_NotKnockedDown_NotLifted_NotKnockedBack()
        {
            foreach (PelagForm form in new[] { PelagForm.AbordageQuake, PelagForm.AbordageGeyser, PelagForm.AbordageBreach })
            {
                var sim = new Simulation(77, 64);
                sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromDouble(5.5));
                const int boss = 1;
                sim.Entities.Stats[boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
                sim.Entities.RefreshStats(boss);
                Give(sim, form);
                sim.Entities.Lavidium[0] = Fix64.FromInt(sim.Entities.MaxLavidium[0]);
                Assert.IsTrue(sim.ValidAbilityTarget(boss, sim.GetAbility(Slot)), form.ToString());
                sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
                sim.Entities.RefreshStats(0);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                FixVec2 at = sim.Entities.Position[boss];
                double gap = double.NaN;
                List<Frame> frames = Cast(sim, boss, 16, before: t =>
                {
                    if (sim.Abordage.Phase == AbordagePhase.Strike && double.IsNaN(gap))
                        gap = (sim.ThicketHullGap(boss, sim.Entities.Position[0]) - sim.Entities.BodyRadius[0]).ToDouble();
                });
                var hook = Of(frames, SimEventType.AbordageHook)[0];
                Assert.IsTrue(hook.ev.Flag, "цель по корпусу");
                var punch = Of(frames, SimEventType.AbordagePunch)[0];
                Assert.IsTrue(punch.ev.Flag, form + ": кулак у груди");
                Assert.That(gap, Is.EqualTo(0.1).Within(0.03), form + ": вплотную к корпусу");
                Assert.IsFalse(sim.Statuses.IsStunned(boss, sim.Tick), form + ": не сбит");
                Assert.IsFalse(sim.AbordageLifted(boss), form + ": не взлетает");
                Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, boss), form + ": не отброшен");
                Assert.That(Dist(sim.Entities.Position[boss], at), Is.LessThan(1e-6));
            }
        }
    }
}
