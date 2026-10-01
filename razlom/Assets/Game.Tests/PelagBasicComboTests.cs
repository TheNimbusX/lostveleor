using System;
using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Contract tests for the opt-in demo basic attack. Legacy arenas remain
    /// unchanged until the animation and effects are accepted by the owner.
    /// Targets are immobile and cannot attack: failures measure the player's
    /// action rather than changes in enemy positioning or damage.
    /// </summary>
    public sealed class PelagBasicComboTests
    {
        const int TargetHealth = 1000000;
        static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        static Simulation Arena(bool combo = true, int damage = 100)
        {
            var sim = new Simulation(4731, 64);
            sim.SetupTestArena(0);
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Facing[0] = At(1, 0);
            sim.Entities.PushWeight[0] = Fix64.Zero;
            sim.Entities.Stats[0].SetBase(StatType.Damage, Fix64.FromInt(damage));
            sim.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            sim.Entities.Stats[0].SetBase(StatType.LavidiumRegen, Fix64.Zero);
            sim.RefreshPlayerStats(false);
            if (combo) sim.EnablePelagBasicCombo();
            sim.Entities.Lavidium[0] = Fix64.FromInt(50);
            sim.SetAbility(0, AbilityDefinition.Cleave(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(1, AbilityDefinition.Whirlwind(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(2, AbilityDefinition.Wreck(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(3, AbilityDefinition.Blaze(), Array.Empty<AbilityNode>(), 0);
            sim.SetAbility(4, AbilityDefinition.Dash(), Array.Empty<AbilityNode>(), 0);
            return sim;
        }

        static int Target(Simulation sim, FixVec2 position, double radius = .1, int armor = 0)
        {
            int id = sim.Entities.Spawn(position, TargetHealth, Faction.Orvill);
            var sheet = sim.Entities.Stats[id];
            sheet.SetBase(StatType.MaxHealth, Fix64.FromInt(TargetHealth));
            sheet.SetBase(StatType.MoveSpeed, Fix64.Zero);
            sheet.SetBase(StatType.AttackSpeed, Fix64.Zero);
            sheet.SetBase(StatType.Armor, Fix64.FromInt(armor));
            sim.Entities.RefreshStats(id);
            sim.Entities.Health[id] = TargetHealth;
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            sim.Entities.PushWeight[id] = Fix64.Zero;
            sim.Entities.BodyRadius[id] = Fix64.FromDouble(radius);
            return id;
        }

        static InputFrame Attack(FixVec2 aim, bool pressed = false, bool held = true, int target = -1)
        {
            var input = InputFrame.Empty;
            input.Aim = aim;
            input.AttackTarget = target;
            input.Flags = (byte)((held ? InputFlags.Attack : 0) | (pressed ? InputFlags.AttackPressed : 0));
            return input;
        }

        static InputFrame Cast(int slot, FixVec2 aim)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = aim;
            return input;
        }

        // Tick is the next unprocessed tick. Advancing to N leaves N pending.
        static void Until(Simulation sim, int nextTick)
        { while (sim.Tick < nextTick) sim.Step(InputFrame.Empty); }
        static void Contact(Simulation sim)
        { Until(sim, sim.PelagBasicAttack.ContactTick + 1); }
        static bool BasicStart(SimEvent e) => e.Type == SimEventType.Attack && e.Source == 0;
        static int Lost(Simulation sim, int target) => TargetHealth - sim.Entities.Health[target];

        [Test]
        public void LegacyModeStaysDefaultAndCandidateEnablePreservesGearModifiers()
        {
            var sim = Arena(false);
            Assert.That(sim.PelagBasicComboEnabled, Is.False);
            sim.Entities.Stats[0].Add(StatModifier.Increased(StatType.AttackSpeed,
                Fix64.Ratio(1, 5), ModifierSource.Equipment, 123));
            sim.EnablePelagBasicCombo();
            Assert.That(sim.Entities.Stats[0].GetBase(StatType.AttackSpeed), Is.EqualTo(Fix64.FromInt(3)));
            Assert.That(sim.Entities.Stats[0].Get(StatType.AttackSpeed).ToDouble(), Is.EqualTo(3.6).Within(.00001));
            ulong hash = sim.StateHash();
            sim.EnablePelagBasicCombo();
            Assert.That(sim.StateHash(), Is.EqualTo(hash), "re-enabling must not restart a live series or add modifiers");
        }

        [Test]
        public void HeldSeriesUsesThreeDistinctContactsAndNeverHitsTwiceInOneSwing()
        {
            var sim = Arena();
            int a = Target(sim, At(1.5, 0)), b = Target(sim, At(1.7, .5));
            var starts = new List<int>(); var stages = new List<int>(); var hits = new List<int>();
            for (int tick = 0; tick < 30; tick++)
            {
                sim.Step(Attack(At(10, 0), tick == 0));
                foreach (var e in sim.Events)
                {
                    if (BasicStart(e))
                    {
                        var action = sim.PelagBasicAttack;
                        starts.Add(action.StartTick); stages.Add(action.Stage);
                        int stage = stages.Count - 1;
                        Assert.That(action.ContactTick - action.StartTick, Is.EqualTo(stage == 2 ? 6 : 4));
                        Assert.That(action.EndTick - action.StartTick, Is.EqualTo(stage == 2 ? 12 : 9));
                    }
                    if (e.Type == SimEventType.Damage && e.DamageOrigin == DamageOrigin.BasicAttack && e.Target == a)
                        hits.Add(tick);
                }
            }
            Assert.That(starts, Is.EqualTo(new[] { 0, 9, 18 }));
            Assert.That(stages, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(hits, Is.EqualTo(new[] { 4, 13, 24 }));
            Assert.That(Lost(sim, a), Is.EqualTo(170)); Assert.That(Lost(sim, b), Is.EqualTo(170));
            Assert.That(sim.Entities.Lavidium[0], Is.EqualTo(Fix64.FromInt(60)), "one finisher refund for all targets");
        }

        [TestCase(1.5, 0, .1, true)]
        [TestCase(1.4, 1.5, .2, true)]
        [TestCase(1.1, 1.9, .1, false)]
        [TestCase(2.35, 0, .2, true)]
        [TestCase(2.5, 0, .2, false)]
        [TestCase(-1.1, 0, .1, false)]
        public void ConeIncludesIntersectingBodiesAndExcludesOutsideOrBehind(double x, double y, double radius, bool hit)
        {
            var sim = Arena(); int victim = Target(sim, At(x, y), radius);
            sim.Step(Attack(At(10, 0), true)); Contact(sim);
            Assert.That(Lost(sim, victim), Is.EqualTo(hit ? 50 : 0));
        }

        [Test]
        public void TargetMayEnterOrLeaveTheConeAfterWindupStarts()
        {
            var sim = Arena(); int entering = Target(sim, At(7, 0)), leaving = Target(sim, At(1.5, 0));
            sim.Step(Attack(At(10, 0), true));
            sim.Entities.Position[entering] = At(1.7, .2); sim.Entities.Position[leaving] = At(7, 0);
            Contact(sim);
            Assert.That(Lost(sim, entering), Is.EqualTo(50)); Assert.That(Lost(sim, leaving), Is.Zero);
        }

        [Test]
        public void MissedFinisherAdvancesSeriesWithoutRefund()
        {
            var sim = Arena(); int target = Target(sim, At(1.5, 0));
            for (int tick = 0; tick < 30; tick++)
            {
                if (tick == 23) sim.Entities.Position[target] = At(8, 0);
                sim.Step(Attack(At(10, 0), tick == 0));
            }
            Assert.That(Lost(sim, target), Is.EqualTo(100));
            Assert.That(sim.Entities.Lavidium[0], Is.EqualTo(Fix64.FromInt(50)));
            sim.Step(Attack(At(10, 0)));
            Assert.That(sim.PelagBasicAttack.Stage, Is.Zero);
        }

        [Test]
        public void SinglePressOnFarTargetDoesNotChaseOrContinueAfterRelease()
        {
            var sim = Arena(); int far = Target(sim, At(8, 0));
            sim.Step(Attack(At(8, 0), true, false, far)); int serial = sim.PelagBasicAttack.Serial;
            Until(sim, 100);
            Assert.That(sim.Entities.Position[0], Is.EqualTo(FixVec2.Zero));
            Assert.That(sim.AttackTarget, Is.EqualTo(-1));
            Assert.That(sim.PelagBasicAttack.Serial, Is.EqualTo(serial));
            Assert.That(Lost(sim, far), Is.Zero);
        }

        [TestCase(1, 0)] [TestCase(0, 1)] [TestCase(-1, 0)] [TestCase(0, -1)]
        public void EachSwingLocksItsOwnAimUntilContact(int x, int y)
        {
            var sim = Arena(); var direction = At(x, y);
            int front = Target(sim, direction * Fix64.Ratio(3, 2));
            int back = Target(sim, direction * Fix64.Ratio(-3, 2));
            sim.Step(Attack(direction * Fix64.FromInt(10), true));
            var attack = sim.PelagBasicAttack;
            while (sim.Tick <= attack.ContactTick) sim.Step(Attack(direction * Fix64.FromInt(-10), false, false));
            Assert.That(sim.PelagBasicAttack.Direction, Is.EqualTo(direction));
            Assert.That(Lost(sim, front), Is.EqualTo(50)); Assert.That(Lost(sim, back), Is.Zero);
        }

        [Test]
        public void AimUnderHeroUsesLastDirectionAndNextSwingCanTurn()
        {
            var sim = Arena(); sim.Entities.Facing[0] = At(0, -1);
            sim.Step(Attack(FixVec2.Zero, true, false));
            Assert.That(sim.PelagBasicAttack.Direction, Is.EqualTo(At(0, -1)));
            Until(sim, 9); sim.Step(Attack(At(-10, 0), true, false));
            Assert.That(sim.PelagBasicAttack.Stage, Is.EqualTo(1));
            Assert.That(sim.PelagBasicAttack.Direction, Is.EqualTo(At(-1, 0)));
        }

        [TestCase(6, true)] [TestCase(1, false)]
        public void FreshPressBuffersOnceForSixTicksButDoesNotSurviveAnOlderPress(int pressTick, bool second)
        {
            var sim = Arena(); sim.Step(Attack(At(10, 0), true, false));
            Until(sim, pressTick); sim.Step(Attack(At(0, 10), true, false));
            Until(sim, 25);
            Assert.That(sim.PelagBasicAttack.Serial, Is.EqualTo(second ? 2 : 1));
            if (second)
            {
                Assert.That(sim.PelagBasicAttack.StartTick, Is.EqualTo(9));
                Assert.That(sim.PelagBasicAttack.Stage, Is.EqualTo(1));
                Assert.That(sim.PelagBasicAttack.Direction, Is.EqualTo(At(0, 1)));
            }
        }

        [Test]
        public void ReleasingAnOrdinaryHoldDoesNotQueueAnotherStrike()
        {
            var sim = Arena();
            for (int tick = 0; tick < 7; tick++) sim.Step(Attack(At(10, 0), tick == 0));
            Until(sim, 30);
            Assert.That(sim.PelagBasicAttack.Serial, Is.EqualTo(1));
        }

        [TestCase(6, 2, 5, 3, 6)]
        [TestCase(300, 2, 4, 2, 4)]
        public void AttackSpeedScalesPreparationAndCycleWithIndependentFloors(int speed, int earlyPrep,
            int earlyCycle, int finalPrep, int finalCycle)
        {
            var sim = Arena(); sim.Entities.Stats[0].SetBase(StatType.AttackSpeed, Fix64.FromInt(speed));
            sim.RefreshPlayerStats(false);
            for (int stage = 0; stage < 3; stage++)
            {
                sim.Step(Attack(At(10, 0), true, false)); var action = sim.PelagBasicAttack;
                Assert.That(action.Stage, Is.EqualTo(stage));
                Assert.That(action.ContactTick - action.StartTick, Is.EqualTo(stage == 2 ? finalPrep : earlyPrep));
                Assert.That(action.EndTick - action.StartTick, Is.EqualTo(stage == 2 ? finalCycle : earlyCycle));
                Until(sim, action.EndTick);
            }
        }

        [Test]
        public void EmptyWindupSlowsMovementOnlyBeforeContactAndDoesNotAdvanceTheHeroByAttack()
        {
            var sim = Arena(); var input = Attack(At(10, 0), true, false);
            input.Flags |= (byte)InputFlags.DirectMovement; input.MoveDirection = At(0, 1);
            sim.Step(input); var action = sim.PelagBasicAttack;
            double step = sim.Entities.MoveStep[0].ToDouble();
            Assert.That(sim.Entities.Velocity[0].Length.ToDouble(), Is.EqualTo(step * .75).Within(.00001));
            input.Flags = (byte)InputFlags.DirectMovement;
            while (sim.Tick < action.ContactTick)
            {
                sim.Step(input);
                Assert.That(sim.Entities.Velocity[0].Length.ToDouble(), Is.EqualTo(step * .75).Within(.00001));
            }
            sim.Step(input); sim.Step(input);
            Assert.That(sim.Entities.Velocity[0].Length.ToDouble(), Is.EqualTo(step).Within(.00001));
            Assert.That(sim.Entities.Position[0].X, Is.EqualTo(Fix64.Zero), "attack does not add a forward dash");
        }

        [TestCase(2, 0, 0)] [TestCase(5, 1, 50)]
        public void RollCancellationKeepsTheCorrectNextStageAndRemovesFutureContacts(int cancelTick,
            int expectedStage, int expectedDamage)
        {
            var sim = Arena(); int victim = Target(sim, At(1.5, 0));
            sim.Step(Attack(At(10, 0), true, false)); Until(sim, cancelTick);
            sim.Step(Cast(4, At(0, 10))); int end = sim.PlayerAction.EndTick;
            Assert.That(sim.PelagBasicAttack.Interrupted, Is.True);
            Until(sim, end + 1);
            Assert.That(Lost(sim, victim), Is.EqualTo(expectedDamage));
            sim.Step(Attack(sim.Entities.Position[0] + At(10, 0), true, false));
            Assert.That(sim.PelagBasicAttack.Stage, Is.EqualTo(expectedStage));
        }

        [TestCase(23, 1)] [TestCase(25, 0)]
        public void ResumeWindowStartsAtTheEndOfTheInterruptingAction(int waitAfterAction, int nextStage)
        {
            var sim = Arena(); sim.Step(Attack(At(10, 0), true, false)); Contact(sim);
            sim.Step(Cast(4, At(0, 10))); int end = sim.PlayerAction.EndTick;
            Until(sim, end + waitAfterAction);
            sim.Step(Attack(sim.Entities.Position[0] + At(10, 0), true, false));
            Assert.That(sim.PelagBasicAttack.Stage, Is.EqualTo(nextStage));
        }

        [TestCase(0, 2)] [TestCase(0, 5)] [TestCase(1, 2)] [TestCase(1, 5)]
        public void BothMobilitySkillsCancelWindupOrRecoveryWithoutAnotherBasicContact(int mobility, int cancelTick)
        {
            var sim = Arena(); Target(sim, At(1.5, 0));
            var definition = mobility == 0 ? AbilityDefinition.Skewer() : AbilityDefinition.Backblast();
            sim.SetAbility(0, definition, Array.Empty<AbilityNode>(), 0);
            sim.Step(Attack(At(10, 0), true, false)); Until(sim, cancelTick);
            sim.Step(Cast(0, At(0, 10))); var action = sim.PlayerAction;
            Assert.That(action.DefinitionId, Is.EqualTo(definition.Id));
            Assert.That(sim.PelagBasicAttack.Interrupted, Is.True);
            while (sim.Tick <= action.EndTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Events, Has.None.Matches<SimEvent>(e => e.Type == SimEventType.Damage
                    && e.Source == 0 && e.DamageOrigin == DamageOrigin.BasicAttack));
            }
            sim.Step(Attack(sim.Entities.Position[0] + At(10, 0), true, false));
            Assert.That(sim.PelagBasicAttack.Stage, Is.EqualTo(cancelTick < 4 ? 0 : 1));
        }

        [Test]
        public void UnavailableRollDoesNotCancelBasicOrLoseItsContact()
        {
            var sim = Arena(); sim.Step(Cast(4, At(0, 10))); Until(sim, sim.PlayerAction.EndTick + 1);
            int target = Target(sim, sim.Entities.Position[0] + At(1.5, 0));
            sim.Step(Attack(sim.Entities.Position[0] + At(10, 0), true, false));
            int serial = sim.PelagBasicAttack.Serial;
            sim.Step(Cast(4, sim.Entities.Position[0] + At(0, 10)));
            Assert.That(sim.PelagBasicAttack.Serial, Is.EqualTo(serial));
            Assert.That(sim.PelagBasicAttack.Interrupted, Is.False);
            Contact(sim); Assert.That(Lost(sim, target), Is.EqualTo(50));
        }

        [Test]
        public void OffensiveSkillWaitsForBasicContactAndDoesNotDeleteThatDamage()
        {
            var sim = Arena(); int target = Target(sim, At(1.5, 0));
            sim.Step(Attack(At(10, 0), true, false)); int basicContact = sim.PelagBasicAttack.ContactTick;
            sim.Step(Cast(0, At(10, 0)));
            Assert.That(sim.PlayerAction.Slot, Is.EqualTo(-1));
            Until(sim, basicContact + 2);
            Assert.That(Lost(sim, target), Is.EqualTo(50));
            Assert.That(sim.PlayerAction.DefinitionId, Is.EqualTo(AbilityDefinition.CleaveId));
            Assert.That(sim.PlayerAction.StartTick, Is.GreaterThanOrEqualTo(basicContact + 1));
        }

        [TestCase(1)] [TestCase(2)]
        public void HeldBasicDoesNotCutOffAnActiveWhirlwindOrWreck(int slot)
        {
            var sim = Arena(); sim.Step(Cast(slot, At(10, 0))); var action = sim.PlayerAction;
            while (sim.Tick < action.EndTick)
            {
                var input = Attack(At(10, 0)); input.AbilityHoldMask = (byte)(1 << slot);
                sim.Step(input);
                Assert.That(sim.Events, Has.None.Matches<SimEvent>(BasicStart));
                Assert.That(sim.PlayerAction.DefinitionId, Is.EqualTo(action.DefinitionId));
            }
        }

        [Test]
        public void StunClearsSeriesBufferAndPendingDamage()
        {
            var sim = Arena(); int target = Target(sim, At(1.5, 0));
            sim.Step(Attack(At(10, 0), true, false)); sim.Step(Attack(At(0, 10), true, false));
            sim.Statuses.ApplyStun(0, sim.Tick + 10);
            Until(sim, 15);
            Assert.That(Lost(sim, target), Is.Zero);
            sim.Step(Attack(At(10, 0), true, false));
            Assert.That(sim.PelagBasicAttack.Stage, Is.Zero);
            Assert.That(sim.PelagBasicAttack.Serial, Is.EqualTo(2));
        }

        [Test]
        public void DeathRemovesPendingHitsAndNewArenaHasNoOldComboState()
        {
            var sim = Arena(); int target = Target(sim, At(1.5, 0));
            sim.Step(Attack(At(10, 0), true, false)); sim.Entities.Alive[0] = false;
            for (int tick = 0; tick < 20; tick++) sim.Step(Attack(At(10, 0)));
            Assert.That(Lost(sim, target), Is.Zero);
            sim.SetupTestArena(0);
            Assert.That(sim.PelagBasicAttack.Serial, Is.Zero);
            sim.Step(Attack(At(10, 0), true, false));
            Assert.That(sim.PelagBasicAttack.Stage, Is.Zero);
        }

        [Test]
        public void CritAndOilUseEachScaledHitWithoutApplyingTheMultiplierTwice()
        {
            var sim = Arena(); int target = Target(sim, At(1.5, 0));
            sim.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.One);
            sim.Entities.Stats[0].SetBase(StatType.CritMultiplier, Fix64.FromInt(2));
            sim.RefreshPlayerStats(false); sim.Step(Cast(3, At(10, 0)));
            Until(sim, sim.PlayerAction.EndTick + 1); Assert.That(sim.BlazeActive, Is.True);
            sim.Step(Attack(At(10, 0), true, false));
            int contact = sim.PelagBasicAttack.ContactTick;
            Until(sim, contact); sim.Step(InputFrame.Empty);
            int physical = 0, fire = 0, hits = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.Damage && e.Target == target)
                {
                    if (e.DamageKind == DamageType.Physical) { physical += e.Amount; hits++; Assert.That(e.Flag, Is.True); }
                    if (e.DamageKind == DamageType.Fire) fire += e.Amount;
                }
            Assert.That(physical, Is.EqualTo(100)); Assert.That(hits, Is.EqualTo(1));
            int expectedFire = CombatStats.RoundToInt(Fix64.FromInt(100)
                * sim.GetAbility(3).Get(AbilityStatType.BonusDamagePercent));
            Assert.That(fire, Is.EqualTo(expectedFire));
        }

        [TestCase(0)] [TestCase(80)]
        public void FrozenLegacyAndComboDpsAreMeasuredOnTheSameEquipmentAndSeed(int armor)
        {
            int Run(bool combo)
            {
                var sim = Arena(combo, 34);
                var equipment = new Equipment(PrototypeContent.Items()); equipment.Bind(sim.Entities.Stats[0]);
                Assert.That(equipment.Equip(new ItemInstance(StableId.Of("base.rusty_sword"), 5,
                    ItemRarity.Normal, 0xCEED), out _), Is.True);
                sim.Entities.Stats[0].Add(StatModifier.More(StatType.CritChance, -Fix64.One, ModifierSource.Buff, 762));
                sim.RefreshPlayerStats(false);
                int target = Target(sim, At(1.5, 0), .1, armor);
                for (int tick = 0; tick < 900; tick++) sim.Step(Attack(At(10, 0), tick == 0, true, target));
                return Lost(sim, target);
            }
            int legacy = Run(false), candidate = Run(true);
            TestContext.WriteLine($"30s armor={armor}: legacy={legacy / 30.0:F3} DPS, combo={candidate / 30.0:F3} DPS; damage {legacy}/{candidate}");
            Assert.That(legacy, Is.GreaterThan(0)); Assert.That(candidate, Is.GreaterThan(0));
            if (armor == 0) Assert.That(Math.Abs(candidate - legacy) / (double)legacy, Is.LessThanOrEqualTo(.05));
        }

        [Test]
        public void ComboReplayIsIdenticalAcrossMovementCancellationAndChangingAim()
        {
            var a = Arena(); var b = Arena();
            Target(a, At(1.5, 0)); Target(b, At(1.5, 0));
            for (int tick = 0; tick < 600; tick++)
            {
                var input = Attack(tick % 90 < 45 ? At(10, 0) : At(0, 10), tick % 17 == 0, tick % 41 < 30);
                input.Flags |= (byte)InputFlags.DirectMovement;
                input.MoveDirection = tick % 120 < 20 ? At(0, .5) : FixVec2.Zero;
                if (tick % 73 == 0) input.AbilityMask = 1 << 4;
                if (tick % 61 == 0) input.AbilityMask = 1;
                if (tick == 231) { a.Statuses.ApplyStun(0, tick + 7); b.Statuses.ApplyStun(0, tick + 7); }
                a.Step(input); b.Step(input);
                Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()), "determinism diverged on tick " + tick);
            }
        }

        [Test]
        public void EventsKeepTheirOwnActionClockWhenAnotherTickStartsTheNextSwing()
        {
            var sim = Arena(); Target(sim, At(1.5, 0));
            sim.Step(Attack(At(10, 0), true));
            SimEvent started = default, damage = default;
            foreach (var e in sim.Events) if (BasicStart(e)) started = e;
            Until(sim, 4); sim.Step(InputFrame.Empty);
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.Damage && e.DamageOrigin == DamageOrigin.BasicAttack) damage = e;
            Until(sim, 9); sim.Step(Attack(At(0, 10), true));
            Assert.That(sim.PelagBasicAttack.Stage, Is.EqualTo(1));
            Assert.That(started.BasicAttackState.Stage, Is.Zero);
            Assert.That(started.BasicAttackState.StartTick, Is.Zero);
            Assert.That(started.BasicAttackState.ContactTick, Is.EqualTo(4));
            Assert.That(damage.BasicAttackState.Serial, Is.EqualTo(started.BasicAttackState.Serial));
            Assert.That(damage.BasicAttackState.ContactProcessed, Is.True);
            Assert.That(damage.BasicAttackState.Direction, Is.EqualTo(At(1, 0)));
        }

        [TestCase(0)] [TestCase(2)]
        public void VoidPhaseSuppressesSameTickAttackAndAlreadyPendingContact(int activationTick)
        {
            var sim = Arena(); int target = Target(sim, At(1.5, 0));
            sim.SetArtifact(RunArtifact.VoidVisage);
            for (int tick = 0; tick < 12; tick++)
            {
                var input = Attack(At(10, 0), tick == 0);
                if (tick == activationTick) input.Flags |= (byte)InputFlags.UseArtifact;
                sim.Step(input);
                if (tick >= activationTick)
                {
                    Assert.That(sim.VoidPhased, Is.True);
                    Assert.That(sim.Events, Has.None.Matches<SimEvent>(BasicStart));
                }
            }
            Assert.That(Lost(sim, target), Is.Zero);
        }
    }
}
