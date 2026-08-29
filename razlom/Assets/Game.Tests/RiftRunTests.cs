using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Петля забега.
    ///
    /// Приёмка задачи: забег проходится от входа до экрана выбора награды
    /// и начинается заново. Плюс правило, ради которого выбор «идти дальше
    /// или уйти» вообще существует: смерть завершает забег, но добытое остаётся.
    /// </summary>
    public class RiftRunTests
    {
        private const ulong Seed = 0x11FEUL;

        private static ModuleSet Modules()
        {
            var entrance = new ModuleDefinition("module.entrance", 4, 4, new[]
            {
                new ModuleConnector(1, 3, Direction.North),
                new ModuleConnector(3, 1, Direction.East),
                new ModuleConnector(2, 0, Direction.South),
                new ModuleConnector(0, 2, Direction.West),
            }, weight: 0, isEntrance: true);

            var hall = new ModuleDefinition("module.hall", 6, 5, new[]
            {
                new ModuleConnector(0, 2, Direction.West),
                new ModuleConnector(5, 2, Direction.East),
                new ModuleConnector(3, 4, Direction.North),
            }, weight: 100);

            var corridor = new ModuleDefinition("module.corridor", 5, 2, new[]
            {
                new ModuleConnector(0, 0, Direction.West),
                new ModuleConnector(4, 1, Direction.East),
            }, weight: 140);

            var junction = new ModuleDefinition("module.junction", 4, 4, new[]
            {
                new ModuleConnector(0, 1, Direction.West),
                new ModuleConnector(3, 2, Direction.East),
                new ModuleConnector(1, 3, Direction.North),
                new ModuleConnector(2, 0, Direction.South),
            }, weight: 90);

            return new ModuleSet(new[] { entrance, hall, corridor, junction });
        }

        private static int SwordId => StableId.Of("base.rusty_sword");

        private static ItemDatabase Items()
        {
            var bases = new[]
            {
                new ItemBaseDefinition(SwordId, ItemCategory.Weapon,
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(5)),
            };

            var affixes = new[]
            {
                new AffixDefinition(StableId.Of("affix.flat_damage"), StableId.Of("group.flat_damage"),
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(1), Fix64.FromInt(9), 1, 100,
                    AffixDefinition.Mask(ItemCategory.Weapon)),
            };

            return new ItemDatabase(bases, affixes);
        }

        private static RiftRun NewRun(ulong seed = Seed)
        {
            var sim = new Simulation(seed, 1024);
            var run = new RiftRun(sim, Modules(), Items(), new[] { SwordId });
            run.StartRun();
            return run;
        }

        /// <summary>
        /// Зачищает Разлом. Ждать, пока игрок перебьёт всех сам, здесь незачем:
        /// проверяется петля, а не бой, и бой проверен своими тестами.
        /// </summary>
        private static void KillAllEnemies(RiftRun run)
        {
            EntityStore entities = run.Sim.Entities;
            for (int i = 0; i < entities.Count; i++)
                if (entities.Side[i] != Faction.Wole) entities.Alive[i] = false;
        }

        private static InputFrame Command(RunCommand command)
            => new InputFrame { Aim = FixVec2.Zero, AbilityMask = 0, Flags = 0, Command = (byte)command };

        private static InputFrame Idle => InputFrame.Empty;

        // ---- приёмка ----

        [Test]
        public void Run_GoesFromEntranceToRewardScreen()
        {
            RiftRun run = NewRun();

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing));
            Assert.That(run.Depth, Is.EqualTo(1));
            Assert.That(run.Map.PlacedCount, Is.GreaterThan(1), "Разлом не собрался");
            Assert.That(run.Sim.CountAliveEnemies(), Is.GreaterThan(0), "врагов не расставили");

            KillAllEnemies(run);
            run.Step(Idle);

            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
            Assert.That(run.RiftsCleared, Is.EqualTo(1));
        }

        [Test]
        public void ChoosingReward_StartsTheNextRift()
        {
            RiftRun run = NewRun();

            KillAllEnemies(run);
            run.Step(Idle);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));

            run.Step(Command(RunCommand.ChooseReward2));

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), "забег не начался заново");
            Assert.That(run.Depth, Is.EqualTo(2), "глубина не выросла");
            Assert.That(run.TakenRewardCount, Is.EqualTo(1));
            Assert.That(run.Sim.CountAliveEnemies(), Is.GreaterThan(0), "новый Разлом пуст");
        }

        [Test]
        public void FullLoop_RunsSeveralRiftsInARow()
        {
            RiftRun run = NewRun();

            for (int rift = 1; rift <= 5; rift++)
            {
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), $"Разлом {rift}");
                Assert.That(run.Depth, Is.EqualTo(rift));

                KillAllEnemies(run);
                run.Step(Idle);

                Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward), $"Разлом {rift} не зачёлся");
                run.Step(Command(RunCommand.ChooseReward1));
            }

            Assert.That(run.RiftsCleared, Is.EqualTo(5));
            Assert.That(run.TakenRewardCount, Is.EqualTo(5));
        }

        [Test]
        public void RewardScreen_AlwaysOffersExactlyThree()
        {
            RiftRun run = NewRun();

            for (int rift = 0; rift < 5; rift++)
            {
                KillAllEnemies(run);
                run.Step(Idle);

                for (int i = 0; i < RiftRun.RewardChoices; i++)
                {
                    RewardOffer offer = run.GetOffer(i);
                    Assert.That((int)offer.Kind, Is.InRange(0, 2), $"Разлом {rift}, предложение {i}");
                }

                run.Step(Command(RunCommand.ChooseReward3));
            }
        }

        // ---- смерть и выход ----

        [Test]
        public void Death_EndsTheRun_ButKeepsWhatWasEarned()
        {
            RiftRun run = NewRun();

            KillAllEnemies(run);
            run.Step(Idle);
            run.Step(Command(RunCommand.ChooseReward1));
            Assert.That(run.TakenRewardCount, Is.EqualTo(1));

            // Умираем во втором Разломе.
            run.Sim.Entities.Alive[Simulation.PlayerId] = false;
            run.Step(Idle);

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Ended));
            Assert.That(run.Outcome, Is.EqualTo(RunOutcome.Died));

            // Награда за пройденное осталась — глубже просто не пойдёшь.
            Assert.That(run.TakenRewardCount, Is.EqualTo(1), "добытое пропало при смерти");
            Assert.That(run.RiftsCleared, Is.EqualTo(1));
        }

        [Test]
        public void DeathOnTheSameTickAsTheLastKill_CountsAsDeath()
        {
            // Труп не получает награду. Правило спорное, но оно должно быть
            // ОДНО, а не «как повезёт с порядком проверок».
            RiftRun run = NewRun();

            KillAllEnemies(run);
            run.Sim.Entities.Alive[Simulation.PlayerId] = false;
            run.Step(Idle);

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Ended));
            Assert.That(run.Outcome, Is.EqualTo(RunOutcome.Died));
        }

        [Test]
        public void Leaving_EndsTheRunWithLoot()
        {
            RiftRun run = NewRun();

            KillAllEnemies(run);
            run.Step(Idle);
            run.Step(Command(RunCommand.ChooseReward1));

            run.Step(Command(RunCommand.Leave));

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Ended));
            Assert.That(run.Outcome, Is.EqualTo(RunOutcome.Left));
            Assert.That(run.TakenRewardCount, Is.EqualTo(1));
        }

        [Test]
        public void LeavingFromTheRewardScreen_AlsoEnds()
        {
            RiftRun run = NewRun();

            KillAllEnemies(run);
            run.Step(Idle);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));

            run.Step(Command(RunCommand.Leave));
            Assert.That(run.Outcome, Is.EqualTo(RunOutcome.Left));
        }

        [Test]
        public void EndedRun_IgnoresFurtherInput()
        {
            RiftRun run = NewRun();
            run.Step(Command(RunCommand.Leave));

            int depth = run.Depth;
            for (int i = 0; i < 50; i++) run.Step(Command(RunCommand.ChooseReward1));

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Ended));
            Assert.That(run.Depth, Is.EqualTo(depth));
        }

        [Test]
        public void RewardScreen_FreezesTheFight()
        {
            // Пока игрок читает награды, бой стоит: иначе он терял бы здоровье
            // за чтение, и экран награды стал бы наказанием.
            RiftRun run = NewRun();
            KillAllEnemies(run);
            run.Step(Idle);

            int tickBefore = run.Sim.Tick;
            for (int i = 0; i < 30; i++) run.Step(Idle);

            Assert.That(run.Sim.Tick, Is.EqualTo(tickBefore), "симуляция шагала на экране награды");
        }

        // ---- детерминизм ----

        [Test]
        public void SameSeed_GivesTheSameRun()
        {
            ulong a = PlayScriptedRun(Seed);
            ulong b = PlayScriptedRun(Seed);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void DifferentSeeds_GiveDifferentRuns()
        {
            Assert.AreNotEqual(PlayScriptedRun(1UL), PlayScriptedRun(2UL));
        }

        /// <summary>Один и тот же сценарий забега: четыре Разлома с разными выборами.</summary>
        private static ulong PlayScriptedRun(ulong seed)
        {
            RiftRun run = NewRun(seed);

            var choices = new[]
            {
                RunCommand.ChooseReward1, RunCommand.ChooseReward3,
                RunCommand.ChooseReward2, RunCommand.ChooseReward1,
            };

            for (int i = 0; i < choices.Length; i++)
            {
                // Немного реального боя перед зачисткой, чтобы в хеш попало
                // и состояние симуляции, а не только фазы петли.
                for (int t = 0; t < 30; t++)
                    run.Step(new InputFrame
                    {
                        Aim = new FixVec2(Fix64.FromInt(5), Fix64.FromInt(5)),
                        AbilityMask = 0,
                        Flags = (byte)InputFlags.MoveOrder,
                        Command = 0
                    });

                KillAllEnemies(run);
                run.Step(Idle);
                run.Step(Command(choices[i]));
            }

            return run.Hash();
        }

        [Test]
        public void StatRewards_ReachTheStatSheet()
        {
            RiftRun run = NewRun();

            // Проходим несколько Разломов, пока не наберём прибавку к стату.
            for (int i = 0; i < 12; i++)
            {
                KillAllEnemies(run);
                run.Step(Idle);
                run.Step(Command(RunCommand.ChooseReward1));
            }

            var sheet = new StatSheet();
            for (int s = 0; s < (int)StatType.Count; s++)
                sheet.SetBase((StatType)s, Fix64.FromInt(100));

            run.ApplyStatRewards(sheet);

            int statRewards = 0;
            for (int i = 0; i < run.TakenRewardCount; i++)
                if (run.GetTaken(i).Kind == RewardKind.StatBoost) statRewards++;

            Assert.That(statRewards, Is.GreaterThan(0), "за двенадцать Разломов не выпало ни одной прибавки");
            Assert.That(sheet.ModifierCount, Is.EqualTo(statRewards));
        }

        [Test]
        public void OfferedItems_ExpandFromTheirRecipe()
        {
            RiftRun run = NewRun();
            var buffer = new GeneratedItem();

            for (int i = 0; i < 12; i++)
            {
                KillAllEnemies(run);
                run.Step(Idle);

                for (int o = 0; o < RiftRun.RewardChoices; o++)
                {
                    RewardOffer offer = run.GetOffer(o);
                    if (offer.Kind != RewardKind.Item) continue;

                    Assert.That(ItemGenerator.Generate(offer.Item, run.Items, buffer), Is.True,
                        "предложенный предмет не разворачивается");
                }

                run.Step(Command(RunCommand.ChooseReward1));
            }
        }

        [Test]
        public void DeeperRifts_AreBigger()
        {
            RiftRun run = NewRun();
            int firstRooms = run.Map.PlacedCount;

            for (int i = 0; i < 6; i++)
            {
                KillAllEnemies(run);
                run.Step(Idle);
                run.Step(Command(RunCommand.ChooseReward1));
            }

            Assert.That(run.Map.PlacedCount, Is.GreaterThan(firstRooms), "Разлом не растёт с глубиной");
        }
    }
}
