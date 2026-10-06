using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.AbordageTests;

namespace Game.Tests
{
    /// <summary>Таланты линии Абордажа на новых сроках (SPEC 3.1) и заготовка Метки.</summary>
    public sealed class AbordageTalentTests
    {
        [Test]
        public void TwoCharges_TheButtonComesBackTheTickAfterTheStrike()
        {
            Simulation sim = Arena(PelagForm.None, 1234, Talent(2));
            int target = Enemy(sim, 5000, 0);
            List<Frame> frames = Run(sim, 15, t => t == 0 || t == 12 ? Press(target) : InputFrame.Empty);
            var casts = Of(frames, SimEventType.AbilityCast);
            Assert.AreEqual(11, Of(frames, SimEventType.AbordagePunch)[0].frame.Tick);
            Assert.AreEqual(2, casts.Count, "запасной заряд");
            Assert.AreEqual(12, casts[1].frame.Tick, "кнопка вернулась тиком после удара");

            Simulation plain = Arena();
            int other = Enemy(plain, 5000, 0);
            Run(plain, 15, t => t == 0 || t == 12 ? Press(other) : InputFrame.Empty);
            Assert.AreEqual(54, plain.AbilityReadyTick(Slot), "без таланта — полная перезарядка");
        }

        [Test]
        public void Interrupt_BreaksTheEnemySwing_OnTheHookTick()
        {
            Simulation sim = Arena(PelagForm.None, 1234, Talent(6));
            int target = Enemy(sim, 5000, 0);
            sim.Entities.PendingAttackTarget[target] = Simulation.PlayerId;
            sim.Entities.AttackImpactTick[target] = 100000;
            var pending = new List<bool>();
            Cast(sim, target, 8, before: t => pending.Add(sim.Entities.PendingAttackTarget[target] == Simulation.PlayerId));
            Assert.IsTrue(pending[6], "до зацепа (тик 6) замах цел");
            Assert.IsFalse(pending[7], "сорван в тик зацепа");
        }

        [Test]
        public void HeavyFist_StunsHalfASecond_QuakeAndGeyserTakeTheLonger_NotTheSum()
        {
            int Stun(PelagForm form)
            {
                Simulation sim = Arena(form, 1234, Talent(1));
                int target = Enemy(sim, 5000, 0);
                List<Frame> frames = Cast(sim, target, 14);
                int ticks = 0, count = 0;
                foreach (var (_, ev) in Of(frames, SimEventType.Stun))
                    if (ev.Target == target) { ticks = ev.Amount; count++; }
                Assert.AreEqual(1, count, form + ": одно оглушение цели");
                return ticks;
            }

            Assert.AreEqual(15, Stun(PelagForm.None));
            Assert.AreEqual(Simulation.AbordageQuakeKnockdownTicks, Stun(PelagForm.AbordageQuake));
            Assert.AreEqual(Simulation.AbordageGeyserLiftTicks, Stun(PelagForm.AbordageGeyser));
            Assert.AreEqual(15, Stun(PelagForm.AbordageBreach));
        }

        [Test]
        public void Momentum_CountsTheRealPull_FistOnly()
        {
            int Fist(int distanceMm, PelagForm form = PelagForm.None)
            {
                Simulation sim = Arena(form, 1234, Talent(5));
                int target = Enemy(sim, distanceMm, 0);
                List<Frame> frames = Cast(sim, target, 20);
                foreach (var (_, ev) in Of(frames, SimEventType.Damage))
                    if (ev.Target == target) return ev.Amount;
                return 0;
            }

            // Тяга 1,6 / 3,6 / 5,6 м: +10 % за целый метр.
            Assert.AreEqual(75 * 110 / 100, Fist(3000));
            Assert.AreEqual(75 * 130 / 100, Fist(5000));
            Assert.AreEqual(75 * 150 / 100, Fist(7000));

            Simulation quake = Arena(PelagForm.AbordageQuake, 1234, Talent(5));
            int a = Enemy(quake, 7000, 0);
            int side = Enemy(quake, 5600, 1600, radiusMm: 450);
            List<Frame> frames2 = Cast(quake, a, 20);
            foreach (var (_, ev) in Of(frames2, SimEventType.Damage))
                if (ev.Target == side) Assert.AreEqual(38, ev.Amount, "волна без «Разгона»");
        }

        [Test]
        public void Hilt_ShortensOtherCooldowns_OnALandedPunch()
        {
            Simulation sim = Arena(PelagForm.None, 1234, Talent(3));
            sim.SetAbility(1, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            int target = Enemy(sim, 5000, 0);
            sim.Step(Press(-1, slot: 1));
            int whirl = sim.AbilityReadyTick(1);
            Run(sim, 40, t => t == 12 ? Press(target) : InputFrame.Empty);
            Assert.AreEqual(whirl - Simulation.TicksPerSecond, sim.AbilityReadyTick(1), "−1 с остальным");
        }

        [Test]
        public void Mark_Trait_TargetTakesThirtyPercentMoreFromPelag_ForThreeSeconds()
        {
            AbilityNode mark = AbilityNode.Trait("test.abordage.mark", AbilityTrait.AbordageMark);
            Simulation sim = Arena(PelagForm.None, 1234, mark);
            int target = Enemy(sim, 5000, 0);
            List<Frame> frames = Cast(sim, target, 12);
            int strike = TickOf(frames, SimEventType.AbordagePunch);
            Assert.IsTrue(sim.AbordageMarked(target));
            int before = sim.Entities.Health[target];
            sim.ApplyAbilityDamage(Simulation.PlayerId, target, 100, Slot, DamageType.Physical);
            Assert.AreEqual(130, before - sim.Entities.Health[target], "×1,30");
            Run(sim, Simulation.AbordageMarkTicks, t => InputFrame.Empty);
            Assert.IsFalse(sim.AbordageMarked(target), "3 с прошли, тик " + sim.Tick + ", удар " + strike);
            before = sim.Entities.Health[target];
            sim.ApplyAbilityDamage(Simulation.PlayerId, target, 100, Slot, DamageType.Physical);
            Assert.AreEqual(100, before - sim.Entities.Health[target]);

            Simulation plain = Arena();
            int other = Enemy(plain, 5000, 0);
            Cast(plain, other, 12);
            Assert.IsFalse(plain.AbordageMarked(other), "без черты метки нет");
        }
    }
}
