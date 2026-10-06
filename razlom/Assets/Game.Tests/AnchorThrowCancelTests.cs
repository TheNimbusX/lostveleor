using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AnchorThrowTests;

namespace Game.Tests
{
    /// <summary>Бросок якоря: срывы, буфер, ходьба (спека §2.5, тест 6).</summary>
    public sealed class AnchorThrowCancelTests
    {
        private static InputFrame Dash(double x, double y) => Press(x, y, PelagKit.DashSlot);

        private static AnchorThrowEnd EndOf(List<Frame> frames, out int tick)
        {
            var ended = Of(frames, SimEventType.AnchorThrowEnded);
            Assert.AreEqual(1, ended.Count, "один конец на каст");
            tick = ended[0].frame.Tick;
            return (AnchorThrowEnd)ended[0].ev.Amount;
        }

        [Test]
        public void DashInFlight_Interrupted_NoTow()
        {
            Simulation sim = Arena();
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            int g = Enemy(sim, 2000, 0);
            FixVec2 at = sim.Entities.Position[g];
            List<Frame> frames = Run(sim, 20, t => t == 0 ? Press(7, 0) : t == 5 ? Dash(0, 5) : InputFrame.Empty);
            Assert.AreEqual(AnchorThrowEnd.Interrupted, EndOf(frames, out int tick));
            Assert.AreEqual(5, tick);
            Assert.AreEqual(1, HitsOf(frames, g).Count, "попадание до срыва осталось");
            Assert.AreEqual(0, Of(frames, SimEventType.AnchorThrowYank).Count, "натяга нет");
            Assert.IsFalse(sim.AnchorThrowReeled(g));
            Assert.Less(Dist(sim.Entities.Position[g], at), 0.02, "никто не тянется");
        }

        [Test]
        public void HeroStunnedInReturn_ReeledCleared_BodiesStandWhereTheyAre()
        {
            Simulation sim = Arena();
            int[] ids = { Enemy(sim, 2000, 0), Enemy(sim, 4000, 0), Enemy(sim, 6000, 0) };
            var stopped = new FixVec2[ids.Length];
            List<Frame> frames = Cast(sim, 7, 0, 22, before: t =>
            {
                if (t == 13) sim.Statuses.ApplyStun(0, sim.Tick + 5);
                if (t == 14) for (int k = 0; k < ids.Length; k++) stopped[k] = sim.Entities.Position[ids[k]];
            });
            Assert.AreEqual(AnchorThrowEnd.Interrupted, EndOf(frames, out int tick));
            Assert.AreEqual(13, tick);
            Assert.AreEqual(0, Of(frames, SimEventType.AnchorThrowCatch).Count);
            for (int k = 0; k < ids.Length; k++)
            {
                Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, ids[k]), "тяга снята " + ids[k]);
                Assert.IsFalse(sim.AnchorThrowReeled(ids[k]));
                Assert.Less(Dist(sim.Entities.Position[ids[k]], stopped[k]), 0.06, "стоит, где был " + ids[k]);
            }
        }

        [Test]
        public void Whirlwind_PressedUpToFiveTicksBeforeCatch_FiresTheTickAfter_ThrowEndsDone()
        {
            for (int early = 5; early <= 6; early++)
            {
                Simulation sim = Arena();
                sim.SetAbility(1, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
                int press = 18 - early;
                List<Frame> frames = Run(sim, 32, t => t == 0 ? Press(7, 0) : t == press ? Press(7, 0, slot: 1) : InputFrame.Empty);
                var casts = Of(frames, SimEventType.AbilityCast).FindAll(c => c.ev.Amount == 1);
                if (early == 5)
                {
                    Assert.AreEqual(1, casts.Count, "из буфера");
                    Assert.AreEqual(19, casts[0].frame.Tick, "тиком после ловли");
                    Assert.AreEqual(AnchorThrowEnd.Done, EndOf(frames, out int tick), "после ловли — Done, не срыв");
                    Assert.AreEqual(19, tick);
                }
                else Assert.AreEqual(0, casts.Count, "буфер 6 тиков — за 6 до ловли не доживает");
            }
        }

        [Test]
        public void Whirlwind_PressedInFlight_IsLost_ThrowCompletes()
        {
            Simulation sim = Arena();
            sim.SetAbility(1, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            List<Frame> frames = Run(sim, 32, t => t == 0 ? Press(7, 0) : t == 5 ? Press(7, 0, slot: 1) : InputFrame.Empty);
            Assert.AreEqual(0, Of(frames, SimEventType.AbilityCast).FindAll(c => c.ev.Amount == 1).Count);
            Assert.AreEqual(AnchorThrowEnd.Done, EndOf(frames, out int tick));
            Assert.AreEqual(27, tick);
        }

        [Test]
        public void Walking_FromTheSecondExitTick_WalkedOut_HeldBefore()
        {
            Simulation sim = Arena();
            List<Frame> frames = Run(sim, 32, t =>
            {
                if (t == 0) return Press(7, 0);
                if (t < 20) return InputFrame.Empty;
                var walk = InputFrame.Empty;
                walk.Flags = (byte)InputFlags.MoveOrder;
                walk.Aim = At(-5, 0);
                walk.AttackTarget = -1;
                walk.AbilityTarget = -1;
                return walk;
            });
            Assert.AreEqual(AnchorThrowEnd.WalkedOut, EndOf(frames, out int tick));
            Assert.AreEqual(23, tick);
            Assert.IsTrue(frames[22].Positions[0].Equals(FixVec2.Zero), "до второго тика выхода — держит");
        }
    }
}
