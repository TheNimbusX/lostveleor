using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.WreckTests;

namespace Game.Tests
{
    /// <summary>Формы Крушения (03.10, SPEC раздел 3; 06.10 вечером): Волнорез, Девятый вал, Призрачный якорь; таланты 3.1.</summary>
    public partial class WreckFormTests
    {
        private static List<SimEvent> Play(Simulation sim, int ticks, System.Func<int, InputFrame> input, System.Action<int> before = null)
        {
            var events = new List<SimEvent>();
            for (int i = 0; i < ticks; i++)
            {
                before?.Invoke(i);
                sim.Step(input(i));
                events.AddRange(sim.Events);
            }
            return events;
        }

        private static int Dealt(List<SimEvent> events, int id)
        {
            int total = 0;
            foreach (var e in events) if (e.Type == SimEventType.Damage && e.Target == id) total += e.Amount;
            return total;
        }

        private static SimEvent First(List<SimEvent> events, SimEventType type, int target = -2)
        {
            foreach (var e in events) if (e.Type == type && (target == -2 || e.Target == target)) return e;
            Assert.Fail("нет события " + type);
            return default;
        }

        private static int Count(List<SimEvent> events, SimEventType type)
        {
            int n = 0;
            foreach (var e in events) if (e.Type == type) n++;
            return n;
        }

        private static InputFrame Fast(int i) => Press(i < 20);

        // ---- Волнорез ----

        [Test]
        public void Breakwater_CarriesALightToTheEndOfEightMetres_CrashHitsHalf()
        {
            var sim = Arena(Form(PelagForm.WreckBreakwater));
            // Выпад v4 везёт героя на 0,6 м: стена от героя после шага — цель на 0,6 дальше, конец стены 8,6.
            int foe = Guardian(sim, 5.6, 0);
            var events = Play(sim, 35, Fast);
            Assert.AreEqual(15, First(events, SimEventType.WreckSlam).Amount, "стена: ⌈(8 − 2,2) / 0,4⌉ = 15 шагов");
            var catchEvent = First(events, SimEventType.WreckBreakwaterCatch, foe);
            Assert.IsTrue(catchEvent.Flag, "лёгкого несёт");
            Assert.AreEqual(11, catchEvent.Amount, "подхват на 5-м шаге: до обрушения 11 тиков");
            Assert.IsTrue(sim.WreckCarried(foe));
            Assert.AreEqual(8.6f, sim.Entities.Position[foe].X.ToFloat(), 0.05f, "донесла до конца стены");
            Assert.AreEqual(0, Count(events, SimEventType.WreckBreakwaterCrash), "обрушение — тиком после последнего шага");

            events.AddRange(Play(sim, 1, i => Press(false)));
            var crash = First(events, SimEventType.WreckBreakwaterCrash);
            Assert.AreEqual(1, crash.Amount);
            Assert.IsFalse(crash.Flag);
            Assert.AreEqual(200, crash.ActionVariant);
            Assert.AreEqual(70 + 35, Dealt(events, foe), "стена 70 + обрушение 35");
            Assert.IsFalse(sim.WreckCarried(foe));
            Assert.IsTrue(sim.Statuses.IsStunned(foe, sim.Tick + 10), "оглушён до обрушения + 15 тиков");
        }

        [Test]
        public void Breakwater_EliteIsHitAndPassed_NotCarried()
        {
            var sim = Arena(Form(PelagForm.WreckBreakwater));
            int foe = Guardian(sim, 5.0, 0);
            sim.MarkElite(foe);
            var events = Play(sim, 60, Fast);
            Assert.IsFalse(First(events, SimEventType.WreckBreakwaterCatch, foe).Flag);
            Assert.AreEqual(70, Dealt(events, foe), "стена бьёт и проходит; обрушение далеко");
            Assert.AreEqual(5f, sim.Entities.Position[foe].X.ToFloat(), 0.01f);
        }

        [Test]
        public void Breakwater_CarriesAtMostSix_NearestFirst()
        {
            var sim = Arena(Form(PelagForm.WreckBreakwater));
            var ids = new int[8];
            for (int k = 0; k < ids.Length; k++)
            {
                ids[k] = Guardian(sim, 3.8 + 0.4 * k, 0);
                sim.Entities.BodyRadius[ids[k]] = Fix64.Ratio(1, 10);
            }
            var events = Play(sim, 60, Fast);
            for (int k = 0; k < ids.Length; k++)
                Assert.AreEqual(k < Simulation.WreckBreakwaterCarryLimit, First(events, SimEventType.WreckBreakwaterCatch, ids[k]).Flag, "враг " + k);
            Assert.AreEqual(6, First(events, SimEventType.WreckBreakwaterCrash).Amount);
        }

        [Test]
        public void Breakwater_StopsBeforeARock_CrashIsFull()
        {
            // Выпад v4 везёт героя на 0,6 м: камень и цель на 0,6 дальше — та же геометрия от героя после шага.
            var sim = RockArena(7.1, 0, 0.5, Form(PelagForm.WreckBreakwater));
            int foe = Guardian(sim, 5.0, 0);
            var events = Play(sim, 60, Fast);
            Assert.IsTrue(sim.Wreck.WallStopped);
            Assert.AreEqual(5.7f, sim.Wreck.WallEnd.ToFloat(), 0.11f, "конец = преграда − 0,3 м");
            var crash = First(events, SimEventType.WreckBreakwaterCrash);
            Assert.IsTrue(crash.Flag, "упёрлась в преграду");
            Assert.AreEqual(70 + 70, Dealt(events, foe), "о преграду — полный урон стены");
        }

        [Test]
        public void Breakwater_HoldsTheHeroToImpactPlus9()
        {
            var x = new List<float>();
            var sim = Arena(Form(PelagForm.WreckBreakwater));
            for (int i = 0; i < 52; i++) { sim.Step(Press(i < 20, 30, 0, walk: true)); x.Add(sim.Entities.Position[P].X.ToFloat()); }
            Assert.AreEqual(x[0], x[16], 1e-6f, "замах выпада: стоит");
            Assert.AreEqual(x[0] + 0.6f, x[29], 1e-3f, "протяжка: стоит по выпад (20) + 9 — после шага выпада v4 0,6 м");
            Assert.Greater(x[30], x[29], "с удар + 10 ходит");
        }

        // ---- Девятый вал (06.10 вечером): заряды — задевшие махи, удержания нет ----

        /// <summary>Нажатия 0 / 6 / 12 (самый быстрый темп), курсор каждого — свой: мах 2 можно увести в сторону.</summary>
        private static InputFrame Series(int i, double swing2X = 5, double swing2Y = 0, bool hold = false)
        {
            bool press = i == 0 || i == 6 || i == 12;
            return i >= 6 && i < 12 ? Press(press, swing2X, swing2Y, hold) : Press(press, 5, 0, hold);
        }

        [Test]
        public void NinthWave_NoSwingLanded_IsTheBaseLunge()
        {
            var sim = Arena(Form(PelagForm.WreckNinthWave));
            // Махи (2,8 м + тело) не достают; круг выпада (точка 2,8, 1,2 + тело) — достаёт.
            int foe = Guardian(sim, 4.6, 0);
            var events = Play(sim, 52, i => Series(i));
            Assert.AreEqual(0, sim.Wreck.NinthCharges);
            Assert.AreEqual(100, sim.Wreck.DamagePercent);
            Assert.AreEqual(0.75f, sim.Wreck.LaneHalfWidth.ToFloat(), 1e-3f);
            Assert.AreEqual(6f, sim.Wreck.LaneLength.ToFloat(), 1e-3f);
            Assert.AreEqual(140, Dealt(events, foe), "только круг выпада, база");
            Assert.IsFalse(First(events, SimEventType.WreckSlam).Flag);
        }

        [Test]
        public void NinthWave_OneSwingLanded_OneAndAHalfDamageAndWidth()
        {
            var sim = Arena(Form(PelagForm.WreckNinthWave));
            int foe = Guardian(sim, 2.0, 0);
            // Мах 2 уведён вверх (+Y): цель на +X вне сектора ±72° — мах мимо, заряд один.
            var events = Play(sim, 52, i => Series(i, 0, 5));
            Assert.AreEqual(1, sim.Wreck.NinthLanded, "задел только мах 1");
            Assert.AreEqual(1, sim.Wreck.NinthCharges);
            Assert.AreEqual(150, sim.Wreck.DamagePercent);
            Assert.AreEqual(1.125f, sim.Wreck.LaneHalfWidth.ToFloat(), 1e-3f, "полоса 1,5 × 1,5 м");
            Assert.AreEqual(6f, sim.Wreck.LaneLength.ToFloat(), 1e-3f, "длина — только с двумя зарядами");
            Assert.AreEqual(70 + 210, Dealt(events, foe), "мах 70 + круг 140 × 1,5");
            Assert.IsFalse(First(events, SimEventType.WreckSlam).Flag);
        }

        [Test]
        public void NinthWave_TwoSwingsLanded_DoubleDamageAndWidth_TwoMetresLonger()
        {
            var sim = Arena(Form(PelagForm.WreckNinthWave));
            int near = Guardian(sim, 2.0, 0);
            int wide = Guardian(sim, 5.5, 2.2);   // поперёк 2,2: база (0,75 + тело) не достаёт, ×2 (1,5 + тело) — да
            int far = Guardian(sim, 8.3, 0);      // вдоль 7,7 от места удара: база (6 + тело) не достаёт, 8 м — да
            var events = Play(sim, 60, i => Series(i));
            Assert.AreEqual(3, sim.Wreck.NinthLanded);
            Assert.AreEqual(2, sim.Wreck.NinthCharges);
            Assert.AreEqual(200, sim.Wreck.DamagePercent);
            Assert.AreEqual(1.5f, sim.Wreck.LaneHalfWidth.ToFloat(), 1e-3f, "полоса 3 м");
            Assert.AreEqual(8f, sim.Wreck.LaneLength.ToFloat(), 1e-3f, "6 + 2 м");
            Assert.AreEqual(1.2f, sim.Wreck.ImpactRadius.ToFloat(), 1e-3f, "круг выпада — базовый");
            Assert.AreEqual(2.8f, sim.Wreck.ImpactPoint.X.ToFloat(), 1e-3f, "точка — базовая: шаг 0,6 + 2,2");
            var slam = First(events, SimEventType.WreckSlam);
            Assert.IsTrue(slam.Flag, "два заряда");
            Assert.AreEqual(12, slam.Amount, "вал ⌈(8 − 2,2) / 0,5⌉ = 12 шагов");
            Assert.AreEqual(70 + 70 + 280, Dealt(events, near), "махи + круг 140 × 2");
            Assert.AreEqual(140, Dealt(events, wide), "вал ×2 и шире");
            Assert.AreEqual(140, Dealt(events, far), "вал на 2 м длиннее");
        }

        [Test]
        public void NinthWave_ChargeIsPerSwing_NotPerEnemy()
        {
            var sim = Arena(Form(PelagForm.WreckNinthWave));
            for (int k = 0; k < 4; k++) Guardian(sim, 1.6 + 0.3 * k, (k - 1.5) * 0.8);
            Play(sim, 40, i => Series(i));
            Assert.AreEqual(2, sim.Wreck.NinthCharges, "четыре цели в каждом махе — всё равно два заряда");
            Assert.AreEqual(200, sim.Wreck.DamagePercent);
        }

        [Test]
        public void NinthWave_SwingsOfThePreviousSeriesDoNotCharge()
        {
            var sim = Arena(Form(PelagForm.WreckNinthWave));
            Guardian(sim, 2.0, 0);
            Play(sim, 60, i => Series(i));
            Assert.AreEqual(2, sim.Wreck.NinthCharges);
            while (sim.Tick < 130) sim.Step(Press(false));
            // Вторая серия: махи уведены вверх, мимо цели — заряды с нуля, у выпада база.
            var events = Play(sim, 30, i => Series(i, 0, 5));
            Assert.AreEqual(1, sim.Wreck.NinthCharges, "мах 1 второй серии задел, мах 2 — мимо");
            Assert.AreEqual(150, sim.Wreck.DamagePercent);
            Assert.AreEqual(1, Count(events, SimEventType.WreckSlam));
        }

        /// <summary>Удержания нет: держи хоть до конца — выпад тот же (8 тиков), заряда и его событий нет.</summary>
        [Test]
        public void NinthWave_HoldingDoesNothing()
        {
            var sim = Arena(Form(PelagForm.WreckNinthWave));
            Guardian(sim, 2.0, 0);
            var strikes = new List<int>();
            var events = new List<SimEvent>();
            for (int i = 0; i < 70; i++)
            {
                int tick = sim.Tick;
                sim.Step(Series(i, hold: true));
                Assert.AreNotEqual(WreckPhase.Charge, sim.Wreck.Phase, "тик " + tick);
                Assert.AreEqual(-1, sim.Wreck.OverheadTick, "тик " + tick);
                foreach (var e in sim.Events) if (e.Type == SimEventType.WreckStage) strikes.Add(tick);
                events.AddRange(sim.Events);
            }
            CollectionAssert.AreEqual(new[] { 5, 11, 20 }, strikes);
            Assert.AreEqual(0, Count(events, SimEventType.WreckChargeStarted));
            Assert.AreEqual(0, Count(events, SimEventType.WreckChargeReleased));
            Assert.AreEqual((int)WreckEnd.Done, First(events, SimEventType.WreckEnded).Amount);
        }
    }
}
