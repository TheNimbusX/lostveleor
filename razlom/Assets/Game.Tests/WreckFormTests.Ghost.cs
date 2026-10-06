using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.WreckTests;

namespace Game.Tests
{
    /// <summary>
    /// Призрачный якорь (06.10 вечером, вместо «Якорной брони») и таланты линии Крушения на новых сроках (SPEC 3.1).
    /// Выпад самого быстрого темпа — удар 20, точка 2,8 (шаг 0,6 + 2,2); якорь падает в 20 + 8 = 28.
    /// </summary>
    public partial class WreckFormTests
    {
        // ---- Призрачный якорь ----

        private static int Taken(params AbilityNode[] nodes)
        {
            var sim = Arena(nodes);
            int foe = Guardian(sim, 1.8, 0);
            Play(sim, 3, Fast);
            int before = sim.Entities.Health[P];
            sim.ApplyAbilityDamage(foe, P, 100, -1, DamageType.Physical);
            return before - sim.Entities.Health[P];
        }

        private static List<int> TicksOf(Simulation sim, int ticks, SimEventType type, List<SimEvent> into, System.Func<int, InputFrame> input = null)
        {
            var at = new List<int>();
            for (int i = 0; i < ticks; i++)
            {
                int tick = sim.Tick;
                sim.Step(input != null ? input(i) : Fast(i));
                foreach (var e in sim.Events) if (e.Type == type) at.Add(tick);
                into.AddRange(sim.Events);
            }
            return at;
        }

        [Test]
        public void GhostAnchor_NoLaneWave_RingLandsEightTicksAfterTheLunge()
        {
            var sim = Arena(Form(PelagForm.WreckGhostAnchor));
            int near = Guardian(sim, 3.0, 0);     // махи, круг выпада и якорь
            int side = Guardian(sim, 2.8, 3.5);   // только якорь: 3,5 от точки ≤ 3 + тело
            int lane = Guardian(sim, 6.8, 0);     // полоса базы его бы задела (вдоль 6,2), якорь — нет (4,0 > 3,85)
            var events = new List<SimEvent>();
            var landed = TicksOf(sim, 50, SimEventType.WreckGhostAnchor, events);

            var slam = First(events, SimEventType.WreckSlam);
            Assert.AreEqual(0, slam.Amount, "вала нет — шагов 0");
            Assert.AreEqual((int)PelagForm.WreckGhostAnchor, slam.ActionVariant);
            CollectionAssert.AreEqual(new[] { 20 + Simulation.WreckGhostDelayTicks }, landed, "одно падение, удар 20 + 8");
            var ghost = First(events, SimEventType.WreckGhostAnchor);
            Assert.AreEqual(300, ghost.Amount, "радиус, см");
            Assert.AreEqual(2.8f, ghost.Position.X.ToFloat(), 1e-3f, "в точку выпада");
            Assert.AreEqual(0f, ghost.Position.Y.ToFloat(), 1e-3f);
            Assert.AreEqual(1, ghost.ActionVariant, "номер серии");
            Assert.AreEqual(-1, sim.Wreck.WaveTick, "фронта нет");
            Assert.AreEqual(-1, sim.Wreck.GhostTick, "упал — больше не ждёт");

            Assert.AreEqual(70 + 70 + 140 + 210, Dealt(events, near), "махи, круг 140, якорь 140 × 1,5");
            Assert.AreEqual(210, Dealt(events, side));
            Assert.AreEqual(0, Dealt(events, lane), "вала по полосе нет");
        }

        [Test]
        public void GhostAnchor_RingStunsForPointSixSeconds_ElitesToo()
        {
            var sim = Arena(Form(PelagForm.WreckGhostAnchor));
            int elite = Guardian(sim, 2.8, 3.5);
            sim.MarkElite(elite);
            var events = new List<SimEvent>();
            TicksOf(sim, 29, SimEventType.WreckGhostAnchor, events);
            var stun = First(events, SimEventType.Stun, elite);
            Assert.AreEqual(Simulation.WreckGhostStunTicks, stun.Amount, "0,6 с = 18 тиков");
            Assert.AreEqual(18, Simulation.WreckGhostStunTicks);
            Assert.IsTrue(sim.Statuses.IsStunned(elite, sim.Tick + 15), "элита оглушена");
            Assert.AreEqual(210, Dealt(events, elite), "якорь 210; «По крупным» не взят");
        }

        [Test]
        public void GhostAnchor_LandsEvenIfTheSeriesIsCutAfterTheLunge()
        {
            var sim = Arena(Form(PelagForm.WreckGhostAnchor));
            sim.SetAbility(4, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            int side = Guardian(sim, 2.8, 3.5);
            var events = new List<SimEvent>();
            var landed = TicksOf(sim, 40, SimEventType.WreckGhostAnchor, events, i =>
            {
                var input = Fast(i);
                if (i == 22) input.AbilityMask = 1 << 4;   // рывок в удержании после выпада
                return input;
            });
            Assert.AreEqual((int)WreckEnd.Interrupted, First(events, SimEventType.WreckEnded).Amount);
            CollectionAssert.AreEqual(new[] { 28 }, landed, "якорь уже брошен — падает, как вал доживает своё");
            Assert.AreEqual(210, Dealt(events, side));
        }

        [Test]
        public void GhostAnchor_NoShellLeft_NoDamageCut_StunNotDeflected()
        {
            Assert.AreEqual(Taken(), Taken(Form(PelagForm.WreckGhostAnchor)), "−40% брони нет");
            var sim = Arena(Form(PelagForm.WreckGhostAnchor));
            Play(sim, 10, Fast);
            Assert.IsTrue(sim.ApplyHeroStun(15), "оглушение в серии не отбивается");
            Assert.IsTrue(sim.HeroStunned);
            Assert.AreEqual(13, (int)PelagForm.WreckGhostAnchor, "номер формы прежний — 13");
        }

        [Test]
        public void GhostAnchor_TwoRunsGiveTheSameEventsAndHashes()
        {
            (List<string> log, ulong hash) Run()
            {
                var sim = Arena(Form(PelagForm.WreckGhostAnchor));
                for (int k = 0; k < 6; k++) Guardian(sim, 1.6 + 0.7 * k, (k % 3 - 1) * 1.4, 400);
                var log = new List<string>();
                for (int i = 0; i < 60; i++)
                {
                    sim.Step(Fast(i));
                    foreach (var e in sim.Events) log.Add(sim.Tick + ":" + e.Type + ":" + e.Target + ":" + e.Amount);
                }
                return (log, sim.StateHash());
            }
            var a = Run();
            var b = Run();
            CollectionAssert.AreEqual(a.log, b.log);
            Assert.AreEqual(a.hash, b.hash);
            Assert.IsTrue(a.log.Exists(s => s.Contains(":" + SimEventType.WreckGhostAnchor + ":")), "якорь упал");
        }

        // ---- таланты линии (3.1) ----

        [Test]
        public void LongWindow_ThirtySixTicks()
        {
            var sim = Arena(Talent(1));
            var events = Play(sim, 50, i => Press(i == 0));
            Assert.AreEqual((int)WreckEnd.WindowExpired, First(events, SimEventType.WreckEnded).Amount);
            Assert.AreEqual(5 + 36 + 1 + 96, sim.AbilityReadyTick(0));
        }

        [Test]
        public void BigGame_SwingsCircleAndWave()
        {
            var sim = Arena(Talent(2));
            int a = Guardian(sim, 3.0, 0), b = Guardian(sim, 5.5, 1.0);
            sim.MarkElite(a);
            sim.MarkElite(b);
            var events = Play(sim, 52, Fast);
            Assert.AreEqual(98 + 98 + 196, Dealt(events, a));
            Assert.AreEqual(98, Dealt(events, b), "вал по элите +40%");
        }

        [Test]
        public void Refund_FifteenForTheSlam()
        {
            Fix64 Left(params AbilityNode[] nodes)
            {
                var sim = Arena(nodes);
                Play(sim, 38, Fast);
                return sim.Entities.Lavidium[P];
            }
            Assert.AreEqual(15f, (Left(Talent(3)) - Left()).ToFloat(), 1e-3f);
        }

        [Test]
        public void FourthStrike_AfterTheSlam_WindupEight_CooldownFromIt()
        {
            var sim = Arena(Talent(4));
            int foe = Guardian(sim, 1.5, 0);
            var strikes = new List<SimEvent>();
            var ticks = new List<int>();
            for (int i = 0; i < 62; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i < 38));
                foreach (var e in sim.Events) if (e.Type == SimEventType.WreckStage) { strikes.Add(e); ticks.Add(tick); }
            }
            CollectionAssert.AreEqual(new[] { 5, 11, 20, 29 }, ticks);
            Assert.IsFalse(strikes[2].Flag, "удар оземь уже не последний");
            Assert.IsTrue(strikes[3].Flag);
            Assert.AreEqual(29 + 96, sim.AbilityReadyTick(0));
        }

        [Test]
        public void Unstoppable_UntilTheSlamHold_NotInTheExit()
        {
            int TakenAt(int tick, bool with)
            {
                var sim = Arena(with ? Talent(5) : new AbilityNode[0]);
                int foe = Guardian(sim, 1.8, 0);
                Play(sim, tick, Fast);
                int before = sim.Entities.Health[P];
                sim.ApplyAbilityDamage(foe, P, 100, -1, DamageType.Physical);
                return before - sim.Entities.Health[P];
            }
            Assert.Less(TakenAt(10, true), TakenAt(10, false), "в серии");
            Assert.Less(TakenAt(22, true), TakenAt(22, false), "в удержании");
            Assert.AreEqual(TakenAt(25, false), TakenAt(25, true), "в выходе — нет");
        }

        [Test]
        public void Momentum_WaveGetsThirtyPercent()
        {
            var sim = Arena(Talent(7));
            int foe = Guardian(sim, 5.5, 0);
            Assert.AreEqual(91, Dealt(Play(sim, 52, Fast), foe));
        }
    }
}
