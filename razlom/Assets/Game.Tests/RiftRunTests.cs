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

        /// <summary>
        /// Зачищает Разлом и доводит игрока до экрана награды.
        ///
        /// Между смертью последнего врага и наградой встал SeekingExit: игрок
        /// обязан дойти до выхода. Тест телепортирует его туда напрямую —
        /// сама ходьба проверяется движковыми тестами, а не петлёй забега.
        /// </summary>
        private static void ClearRiftAndReachExit(RiftRun run)
        {
            KillAllEnemies(run);
            run.Step(Idle);

            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            run.Step(Idle);
        }

        private static InputFrame Command(RunCommand command)
            => new InputFrame { Aim = FixVec2.Zero, AbilityMask = 0, Flags = 0, Command = (byte)command };

        private static InputFrame Idle => InputFrame.Empty;

        /// <summary>
        /// Берёт награду. Если это способность при полной панели, разбирает её:
        /// петле забега важен выбор, а не какую кнопку заменить.
        /// </summary>
        private static void Take(RiftRun run, RunCommand choice)
        {
            run.Step(Command(choice));
            if (run.Phase == RunPhase.ReplacingAbility) run.Step(Command(RunCommand.SalvageAbility));
        }

        // ---- приёмка ----

        [Test]
        public void Run_GoesFromEntranceToRewardScreen()
        {
            RiftRun run = NewRun();

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing));
            Assert.That(run.Depth, Is.EqualTo(1));
            Assert.That(run.Map.PlacedCount, Is.GreaterThan(1), "Разлом не собрался");
            Assert.That(run.Sim.CountAliveEnemies(), Is.GreaterThan(0), "врагов не расставили");

            ClearRiftAndReachExit(run);

            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
            Assert.That(run.RiftsCleared, Is.EqualTo(1));
        }

        [Test]
        public void FullLoop_RunsSeveralRiftsInARow()
        {
            RiftRun run = NewRun();

            for (int rift = 1; rift <= 5; rift++)
            {
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing), $"Разлом {rift}");
                Assert.That(run.Depth, Is.EqualTo(rift));

                ClearRiftAndReachExit(run);

                Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward), $"Разлом {rift} не зачёлся");
                Take(run, RunCommand.ChooseReward1);
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
                ClearRiftAndReachExit(run);

                for (int i = 0; i < RiftRun.RewardChoices; i++)
                {
                    RewardOffer offer = run.GetOffer(i);
                    Assert.IsTrue(offer.Kind == RewardKind.Item || offer.Kind == RewardKind.Ability
                                  || offer.Kind == RewardKind.Talent, $"Разлом {rift}, предложение {i}: {offer.Kind}");
                }

                Take(run, RunCommand.ChooseReward3);
            }
        }

        // ---- смерть и выход ----

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
        public void RewardScreen_FreezesTheFight()
        {
            // Пока игрок читает награды, бой стоит: иначе он терял бы здоровье
            // за чтение, и экран награды стал бы наказанием.
            RiftRun run = NewRun();
            ClearRiftAndReachExit(run);

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

                ClearRiftAndReachExit(run);
                Take(run, choices[i]);
            }

            return run.Hash();
        }

    }
}
