using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>Крушение v2: ноги, срывы, босс, события и снимок, хеш (SPEC 2.10, тесты 6–9).</summary>
    public partial class WreckTests
    {
        private static InputFrame Slots(int mask, double aimX = 5, double aimY = 0)
        {
            var input = Press(false, aimX, aimY);
            input.AbilityMask = (byte)mask;
            input.AbilityHoldMask = (byte)mask;
            return input;
        }

        // ---- 6. Ноги ----

        [Test]
        public void Legs_HeldInWindupAndFollow_WalkFullSpeedInTheWindow()
        {
            var sim = Arena();
            var x = new List<float>();
            for (int i = 0; i < 26; i++)
            {
                sim.Step(Press(i == 0, 30, 0, walk: true));
                x.Add(sim.Entities.Position[P].X.ToFloat());
            }
            for (int t = 1; t <= 7; t++) Assert.AreEqual(x[0], x[t], 1e-6f, "тик " + t + ": замах 5 и 2 тика проводки — стоит");
            Assert.Greater(x[8], x[7], "в окне ходит");
            Assert.AreEqual(sim.Entities.MoveStep[P].ToFloat(), x[25] - x[24], 1e-3f, "в окне — полный шаг 0,15");
        }

        [TestCase(true)] [TestCase(false)]
        public void Legs_LungeHoldsToTick25_WalkFromTick26BreaksTheExit(bool walk)
        {
            var sim = Arena();
            var x = new List<float>();
            int ended = -1, reason = -1;
            for (int i = 0; i < 52; i++)
            {
                int tick = sim.Tick;
                sim.Step(Press(i < 20, 30, 0, walk: walk));
                x.Add(sim.Entities.Position[P].X.ToFloat());
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.WreckEnded) { ended = tick; reason = e.Amount; }
            }
            if (!walk)
            {
                Assert.AreEqual(31, ended, "выход доигран: выпад 20 + 3 + 8");
                Assert.AreEqual((int)WreckEnd.Done, reason);
                return;
            }
            // Серия держит героя; выпад v4 — шаг Sim 0,15 м в тиках 17–20 (Simulation.Wreck.Lunge), дальше стоит.
            for (int t = 1; t <= 25; t++)
                Assert.AreEqual(x[0] + 0.15f * System.Math.Clamp(t - 16, 0, 4), x[t], 1e-3f, "тик " + t + ": серия держит героя");
            Assert.Greater(x[26], x[25], "с тика 26 ходит");
            Assert.AreEqual(26, ended);
            Assert.AreEqual((int)WreckEnd.WalkedOut, reason);
        }

        // ---- 7. Срывы ----

        [TestCase("stun")] [TestCase("dash")] [TestCase("knockback")] [TestCase("damage")]
        public void Interrupts_StunAndDashBreak_KnockbackAndDamageDoNot(string kind)
        {
            var sim = Arena();
            sim.SetAbility(4, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            Guardian(sim, 1.8, 0);
            var strikes = new List<int>();
            int reason = -1;
            for (int i = 0; i < 60; i++)
            {
                int mask = i == 0 || i == 5 || i == 11 ? 1 : 0;
                if (i == 10)
                {
                    if (kind == "stun") sim.ApplyHeroStun(15);
                    else if (kind == "dash") mask = 1 << 4;
                    else if (kind == "knockback")
                        ForcedMotion.Begin(sim.Entities, P, sim.Entities.Position[P] - new FixVec2(M(1.5), Fix64.Zero), 6, ForcedMotionKind.Knockback);
                    else sim.Entities.Health[P] -= 40;
                }
                int tick = sim.Tick;
                sim.Step(Slots(mask));
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.WreckStage) strikes.Add(tick);
                    if (e.Type == SimEventType.WreckEnded) reason = e.Amount;
                }
            }
            if (kind == "stun" || kind == "dash")
            {
                CollectionAssert.AreEqual(new[] { 5 }, strikes);
                Assert.AreEqual((int)WreckEnd.Interrupted, reason);
                Assert.AreEqual(96, sim.AbilityReadyTick(0), "кулдаун от каста");
            }
            else
            {
                CollectionAssert.AreEqual(new[] { 5, 11, 20 }, strikes, "серия доиграна в прежние сроки");
            }
        }

        [TestCase(3, 6)]
        [TestCase(12, 12)]
        public void OtherSkill_BeforeTheStrikeAfterIt_AfterTheStrikeAtOnce(int press, int cast)
        {
            var sim = Arena();
            sim.SetAbility(1, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            int castTick = -1, ended = -1;
            for (int i = 0; i < 20; i++)
            {
                int tick = sim.Tick;
                sim.Step(Slots(i == 0 ? 1 : i == press ? 2 : 0));
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.AbilityCast && e.Amount == 1) castTick = tick;
                    if (e.Type == SimEventType.WreckEnded && e.Amount == (int)WreckEnd.Interrupted) ended = tick;
                }
            }
            Assert.AreEqual(cast, castTick);
            Assert.AreEqual(cast, ended, "серия снята тем же тиком");
        }

        /// <summary>
        /// Выпад v4 (Simulation.Wreck.Lunge): шаг 0,6 м в тиках 17–20, как выпад сабли; след HUD до первого нажатия
        /// показывает точку там, куда ляжет якорь (2,2 м перед героем после шага), и совпадает с ударом.
        /// </summary>
        [Test]
        public void Lunge_StepsSixTenths_PreviewBeforeTheSeriesIsWhereTheAnchorLands()
        {
            var sim = Arena();
            Assert.IsTrue(sim.WreckLanePreview(0, new FixVec2(M(5), Fix64.Zero), out _, out _, out Fix64 from, out Fix64 to,
                out _, out FixVec2 preview, out _));
            Assert.AreEqual(2.8f, from.ToFloat(), 1e-3f, "до нажатия: 0,6 шага + 2,2");
            Assert.AreEqual(6.6f, to.ToFloat(), 1e-3f, "полоса 6 м от героя после шага");
            var x = new List<float>();
            for (int i = 0; i < 24; i++) { sim.Step(Press(i < 20)); x.Add(sim.Entities.Position[P].X.ToFloat()); }
            Assert.AreEqual(0f, x[16], 1e-6f, "до последних 4 тиков замаха выпада — стоит");
            Assert.AreEqual(0.15f, x[17], 1e-3f);
            Assert.AreEqual(0.6f, x[20], 1e-3f, "к удару — 0,6");
            Assert.AreEqual(0.6f, x[23], 1e-3f, "после удара не едет");
            Assert.AreEqual(preview.X.ToFloat(), sim.Wreck.ImpactPoint.X.ToFloat(), 1e-3f, "удар — в точке следа");
        }
    }
}
