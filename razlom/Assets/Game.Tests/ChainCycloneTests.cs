using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public class ChainCycloneTests
    {
        private static Simulation Arena()
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            sim.SetAbility(2, AbilityDefinition.ChainCyclone(), new AbilityNode[0], 0);
            sim.SetAbility(3, AbilityDefinition.ChainStep(), new AbilityNode[0], 0);
            sim.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            sim.Entities.RefreshStats(0);
            return sim;
        }

        private static int Enemy(Simulation sim, int x, int y = 0)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y)), 10000, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static InputFrame Hold(bool start = false)
        {
            var frame = InputFrame.Empty;
            frame.AbilityMask = start ? (byte)4 : (byte)0;
            frame.AbilityHoldMask = 4;
            frame.Aim = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            return frame;
        }

        [Test]
        public void SquallAnnouncesEachHopAndOneCompletion()
        {
            var sim = Arena();
            int target = Enemy(sim, 2);
            var cast = InputFrame.Empty;
            cast.AbilityMask = 8;
            cast.AbilityTarget = target;
            var remaining = new System.Collections.Generic.List<int>();
            var indices = new System.Collections.Generic.List<int>();
            for (int tick = 0; tick < 35; tick++)
            {
                sim.Step(tick == 0 ? cast : InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.ChainStepHop)
                    {
                        remaining.Add(ev.Amount);
                        indices.Add(ev.ActionVariant);
                    }
            }
            CollectionAssert.AreEqual(new[] { 4, 3, 2, 1, 0 }, remaining);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 3 }, indices);
        }

        [Test]
        public void SquallContinuesAnimationWhenTargetDiesBeforeContact()
        {
            var sim = Arena();
            int target = Enemy(sim, 2);
            int next = Enemy(sim, 3, 1);
            var cast = InputFrame.Empty;
            cast.AbilityMask = 8;
            cast.AbilityTarget = target;
            sim.Step(cast);
            sim.Entities.Alive[target] = false;
            bool continued = false;
            for (int tick = 0; tick < AnchorKit.ChainTicksPerHop + 2; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.ChainStepHop && ev.Amount == 3)
                    {
                        Assert.AreEqual(next, ev.Target);
                        continued = true;
                    }
            }
            Assert.IsTrue(continued, "Следующая анимация не должна зависеть от попадания по мёртвой цели.");
        }

        [Test]
        public void RadiusGrowsRotationSlowsAndStopsAtMaximum()
        {
            var sim = Arena();
            sim.Step(Hold(true));
            Fix64 radius = sim.CycloneRadius;
            Fix64 angle = sim.CycloneAngle;
            sim.Step(Hold());
            Fix64 initialSpeed = sim.CycloneAngle - angle;
            for (int i = 2; i < 59; i++) sim.Step(Hold());
            angle = sim.CycloneAngle;
            sim.Step(Hold());
            Assert.Greater(sim.CycloneRadius.Raw, radius.Raw);
            Assert.Less((sim.CycloneAngle - angle).Raw, initialSpeed.Raw);
            sim.Step(Hold());
            Assert.IsFalse(sim.CycloneActive);
        }

        [Test]
        public void ReleaseStopsDamageAndKeepsCooldownIncludingBetweenTickTap()
        {
            var sim = Arena();
            int target = Enemy(sim, 1);
            var tap = Hold(true); tap.AbilityHoldMask = 0;
            sim.Step(tap);
            Assert.AreEqual(10000, sim.Entities.Health[target]);
            Assert.IsFalse(sim.CycloneActive);
            Assert.Greater(sim.AbilityReadyTick(2), sim.Tick);
        }

        [Test]
        public void ChainHitsInsideRadiusWithoutForcedMotionAndRepeatsOnNextPass()
        {
            var sim = Arena();
            int target = Enemy(sim, 1);
            sim.Step(Hold(true));
            int afterFirst = sim.Entities.Health[target];
            Assert.Less(afterFirst, 10000);
            sim.Step(Hold());
            Assert.AreEqual(afterFirst, sim.Entities.Health[target]);
            for (int i = 2; i < 38; i++) sim.Step(Hold());
            Assert.Less(sim.Entities.Health[target], afterFirst);
            Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[target]);
        }

        [Test]
        public void InvalidSquallDoesNotInterruptCycloneOrSpendCooldown()
        {
            var sim = Arena(); sim.Step(Hold(true));
            var frame = Hold(); frame.AbilityMask = 8; frame.AbilityTarget = -1;
            sim.Step(frame);
            Assert.IsTrue(sim.CycloneActive);
            Assert.AreEqual(0, sim.AbilityReadyTick(3));
        }

        [Test]
        public void SquallHitsOneEnemyFourTimesAndInterruptsCyclone()
        {
            var sim = Arena(); int target = Enemy(sim, 3);
            sim.Step(Hold(true));
            var frame = Hold(); frame.AbilityMask = 8; frame.AbilityTarget = target;
            sim.Step(frame);
            Assert.IsFalse(sim.CycloneActive);
            int hits = 0;
            for (int i = 0; i < 25; i++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.Damage && ev.Target == target) hits++;
            }
            Assert.AreEqual(4, hits);
        }

        [Test]
        public void ReplaysAndArenaResetIncludeCycloneState()
        {
            var a = Arena(); var b = Arena();
            for (int i = 0; i < 24; i++) { Enemy(a, 1 + i % 3, i / 3 - 4); Enemy(b, 1 + i % 3, i / 3 - 4); }
            for (int i = 0; i < 65; i++)
            {
                a.Step(Hold(i == 0)); b.Step(Hold(i == 0));
                Assert.AreEqual(a.StateHash(), b.StateHash());
            }
            a.SetupTestArena(0);
            Assert.IsFalse(a.CycloneActive);
        }

        [Test]
        public void FullPassDamagesMoreThanSixteenHeavyTargetsWithoutMovingThem()
        {
            var sim = Arena();
            var positions = new FixVec2[24];
            for (int i = 0; i < positions.Length; i++)
            {
                int id = Enemy(sim, 2);
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio(i, positions.Length);
                positions[i] = new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle)) * Fix64.Ratio(15, 10);
                sim.Entities.Position[id] = positions[i];
                sim.Entities.BodyRadius[id] = Fix64.Ratio(4, 100);
                sim.Entities.PushWeight[id] = Fix64.FromInt(2);
            }
            for (int tick = 0; tick < 20; tick++) sim.Step(Hold(tick == 0));
            for (int i = 0; i < positions.Length; i++)
            {
                Assert.Less(sim.Entities.Health[i + 1], 10000, "Target " + i);
                Assert.AreEqual(positions[i], sim.Entities.Position[i + 1]);
                Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[i + 1]);
            }
        }

        [Test]
        public void SustainedContactNeverHitsTwiceWithinTheSameTurn()
        {
            var sim = Arena(); int target = Enemy(sim, 1);
            sim.Entities.BodyRadius[target] = Fix64.Ratio(1, 100);
            sim.Entities.Position[target] = new FixVec2(Fix64.Ratio(5, 100), Fix64.Zero);
            sim.Entities.PushWeight[0] = sim.Entities.PushWeight[target] = Fix64.Zero;
            var hits = new int[5];
            for (int tick = 0; tick < 60; tick++)
            {
                Fix64 before = sim.CycloneTravel;
                sim.Step(Hold(tick == 0));
                int turn = (before / Fix64.TwoPi).ToInt();
                int endTurn = (sim.CycloneTravel / Fix64.TwoPi).ToInt();
                int contacts = 0;
                foreach (var ev in sim.Events) if (ev.Type == SimEventType.Damage && ev.Target == target) contacts++;
                if (turn == endTurn) hits[turn] += contacts;
                // Контакт на шве относится к одному из соседних оборотов.
                else Assert.LessOrEqual(contacts, 1);
            }
            foreach (int count in hits) Assert.LessOrEqual(count, 1);
        }

        [Test]
        public void SquallSingletonUsesDifferentLandingPositions()
        {
            var sim = Arena(); int target = Enemy(sim, 3);
            var cast = InputFrame.Empty; cast.AbilityMask = 8; cast.AbilityTarget = target;
            sim.Step(cast);
            FixVec2 previous = sim.Entities.Position[0];
            int hits = 0;
            for (int tick = 0; tick < 25; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.Damage && ev.Target == target)
                    {
                        Assert.Greater((sim.Entities.Position[0] - previous).Length.ToFloat(), .5f);
                        previous = sim.Entities.Position[0]; hits++;
                    }
            }
            Assert.AreEqual(4, hits);
        }

        [Test]
        public void DeadSquallTargetIsSkippedAndNoCandidatesEndsSeries()
        {
            var sim = Arena(); int target = Enemy(sim, 3);
            var cast = InputFrame.Empty; cast.AbilityMask = 8; cast.AbilityTarget = target;
            sim.Step(cast); sim.Entities.Alive[target] = false;
            for (int tick = 0; tick < 20; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events) Assert.IsFalse(ev.Type == SimEventType.Damage && ev.Target == target);
            }
            Assert.AreEqual(-1, sim.ChainTargetId);
            Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[0]);
            Assert.Greater(sim.AbilityReadyTick(3), sim.Tick);
        }

        [Test]
        public void FortyTargetCycloneAllocatesNoManagedMemoryAfterWarmup()
        {
            var sim = Arena();
            for (int i = 0; i < 40; i++) Enemy(sim, i % 6 - 3, i / 6 - 3);
            for (int i = 0; i < 120; i++) sim.Step(Hold(i == 0));
            var start = Hold(true); var held = Hold();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            sim.Step(start);
            for (int i = 1; i < 60; i++) sim.Step(held);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
        }

        [Test]
        public void SquallCannotCrossClosedGapOrDamageThroughIt()
        {
            var room = new ModuleDefinition("module.squall_wall", 2, 2,
                new ModuleConnector[0], weight: 0, isEntrance: true);
            var map = new LayoutMap(new ModuleSet(new[] { room }), 2);
            map.TryPlace(0, 0, 0, 0); map.TryPlace(0, 0, 3, 0);
            var sim = Arena(); sim.SetupRift(map, 1234, enemiesPerRoom: 0, enemyHealth: 100);
            int target = Enemy(sim, 6, 2);
            sim.Entities.Position[0] = new FixVec2(Fix64.FromInt(3), Fix64.FromInt(2));
            sim.Entities.Position[target] = new FixVec2(Fix64.Ratio(65, 10), Fix64.FromInt(2));
            var cast = InputFrame.Empty; cast.AbilityMask = 8; cast.AbilityTarget = target;
            sim.Step(cast);
            for (int tick = 0; tick < 26; tick++) sim.Step(InputFrame.Empty);
            Assert.Less(sim.Entities.Position[0].X.ToFloat(), 4f);
            Assert.AreEqual(10000, sim.Entities.Health[target]);
            Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[0]);
        }

        [Test]
        public void SquallTargetSequenceIsDeterministicAndPrefersUnvisited()
        {
            var a = Arena(); var b = Arena();
            for (int i = 0; i < 4; i++) { Enemy(a, 2 + i % 2, i / 2 * 2); Enemy(b, 2 + i % 2, i / 2 * 2); }
            var cast = InputFrame.Empty; cast.AbilityMask = 8; cast.AbilityTarget = 1;
            a.Step(cast); b.Step(cast);
            var hit = new bool[5]; int count = 0;
            for (int tick = 0; tick < 25; tick++)
            {
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                Assert.AreEqual(a.StateHash(), b.StateHash());
                Assert.AreEqual(a.ChainTargetId, b.ChainTargetId);
                foreach (var ev in a.Events)
                    if (ev.Type == SimEventType.Damage && ev.Source == 0)
                    {
                        Assert.IsFalse(hit[ev.Target], "Повтор до обхода доступных целей");
                        hit[ev.Target] = true; count++;
                    }
            }
            Assert.AreEqual(4, count);
        }
    }
}
