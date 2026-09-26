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
            // Встреча по шаблону выходит волнами: следующая встаёт, когда
            // прежняя легла, — зачищаем, пока арена не отпустит.
            for (int guard = 0; guard < 4000; guard++)
            {
                KillAllEnemies(run);
                run.Step(Idle);
                if (run.Phase != RunPhase.Clearing) break;
            }

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

        /// <summary>Первая карточка на экране, которая не родник.</summary>
        private static RunCommand NotSpring(RiftRun run)
        {
            for (int i = 0; i < RiftRun.RewardChoices; i++)
                if (run.GetOffer(i).Kind != RewardKind.Spring) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
            Assert.Fail("на экране одни родники");
            return RunCommand.None;
        }

        // ---- приёмка ----

        private static RiftRun NewArenaRun()
        {
            var modules = Modules();
            var level = new RiftLevelSettings(12, 1, 0, 0, 1, 2, 100, arenaSize: 3);
            var location = new LocationDefinition(42, modules, new[] { level, level, level }, completeAtEnd: true);
            var run = new RiftRun(new Simulation(Seed, 1024), modules, Items(), new[] { SwordId }, location: location);
            run.StartRun();
            return run;
        }

        [Test]
        public void ArenaFlow_OffersRoutesThenCompletesFinalLevel()
        {
            var run = NewArenaRun();
            for (int depth = 1; depth <= 3; depth++)
            {
                Assert.That(run.Depth, Is.EqualTo(depth));
                Assert.That(run.Map.ExitCount, Is.EqualTo(1));
                Assert.That(run.Map.RewardBranchCount, Is.Zero);
                ClearRiftAndReachExit(run);
                Take(run, RunCommand.ChooseReward1);
                if (depth == 3) break;
                Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingRoute));
                int tick = run.Sim.Tick;
                run.Step(Command(RunCommand.ChooseReward1));
                Assert.That(run.Depth, Is.EqualTo(depth));
                Assert.That(run.Sim.Tick, Is.EqualTo(tick));
                run.Step(Command(RunCommand.ChooseRoute2));
                Assert.That(run.CurrentRoute.Reward, Is.EqualTo(ArenaReward.Shop));
            }
            Assert.That(run.Outcome, Is.EqualTo(RunOutcome.Completed));
        }

        [Test]
        public void ArenaFlow_HardRouteGrantsBonusOnceAfterClear()
        {
            var run = NewArenaRun();
            ClearRiftAndReachExit(run);
            Take(run, RunCommand.ChooseReward1);
            var offer = run.GetRoute(2);
            run.Step(Command(RunCommand.ChooseRoute3));
            Assert.That(run.CurrentRoute.Hard, Is.True);
            // Хранитель 550 × 100% уровня × 125% «Сложно».
            Assert.That(run.Sim.Entities.MaxHealth[1], Is.EqualTo(EnemyArchetypes.ScaleHealth(550, 100, 125)));
            Assert.That(run.Sim.Entities.MaxHealth[1], Is.EqualTo(688));
            int before = run.Gold;
            KillAllEnemies(run);
            run.Step(Idle);
            Assert.That(run.Gold, Is.EqualTo(before + offer.BonusGold));
            run.Step(Idle);
            Assert.That(run.Gold, Is.EqualTo(before + offer.BonusGold));
        }

        [Test]
        public void ArenaFlow_ReplaysRouteDecisionsDeterministically()
        {
            var a = NewArenaRun(); var b = NewArenaRun();
            for (int depth = 1; depth < 3; depth++)
            {
                ClearRiftAndReachExit(a); ClearRiftAndReachExit(b);
                Take(a, RunCommand.ChooseReward1); Take(b, RunCommand.ChooseReward1);
                Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
                a.Step(Command(RunCommand.ChooseRoute3)); b.Step(Command(RunCommand.ChooseRoute3));
                Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            }
        }

        // ---- перенос здоровья между аренами (владелец, 26.09) ----

        [Test]
        public void ArenaFlow_CarriesMissingHealthToNextArena_AndNewRunStartsFull()
        {
            var run = NewArenaRun();
            EntityStore e = run.Sim.Entities;
            Assert.That(e.Health[0], Is.EqualTo(e.MaxHealth[0]), "забег начинается с полным здоровьем");
            e.Health[0] = e.MaxHealth[0] - 40;
            ClearRiftAndReachExit(run);
            Take(run, RunCommand.ChooseReward1);
            run.Step(Command(RunCommand.ChooseRoute1));
            Assert.That(run.Depth, Is.EqualTo(2));
            // Переезжает недостача: полученные 40 остаются полученными, а
            // прибавка награды к максимуму (если выпала) дошла бы и до текущего.
            Assert.That(e.Health[0], Is.EqualTo(e.MaxHealth[0] - 40), "дверь арены не лечит");
            Assert.That(e.Lavidium[0], Is.EqualTo(Fix64.FromInt(e.MaxLavidium[0])), "лавидий по-прежнему полон");

            run.StartRun();
            Assert.That(run.Depth, Is.EqualTo(1));
            Assert.That(e.Health[0], Is.EqualTo(e.MaxHealth[0]), "новый забег начинается с полным здоровьем");
        }

        [Test]
        public void PrototypeFlow_CarriesMissingHealth_AndNeverEntersDead()
        {
            var run = NewRun();
            EntityStore e = run.Sim.Entities;
            e.Health[0] = 1;
            ClearRiftAndReachExit(run);
            // Родник на экране вылечил бы: берётся любая другая карточка.
            Take(run, NotSpring(run));
            Assert.That(run.Depth, Is.EqualTo(2));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Clearing));
            // Недостача больше нового максимума не убивает на входе.
            Assert.That(e.Health[0], Is.EqualTo(System.Math.Max(1, e.MaxHealth[0] - (1000 - 1))));
            Assert.That(e.Alive[0], Is.True);
            Assert.That(run.Sim.PlayerMissingHealth, Is.EqualTo(e.MaxHealth[0] - e.Health[0]));
        }

        [Test]
        public void CarriedHealth_ReplaysDeterministically()
        {
            var a = NewArenaRun(); var b = NewArenaRun();
            a.Sim.Entities.Health[0] -= 77; b.Sim.Entities.Health[0] -= 77;
            ClearRiftAndReachExit(a); ClearRiftAndReachExit(b);
            Take(a, RunCommand.ChooseReward1); Take(b, RunCommand.ChooseReward1);
            a.Step(Command(RunCommand.ChooseRoute3)); b.Step(Command(RunCommand.ChooseRoute3));
            Assert.That(a.Sim.Entities.Health[0], Is.EqualTo(a.Sim.Entities.MaxHealth[0] - 77));
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
        }

        [Test]
        public void BigAttackTokens_FollowArenaNumber()
        {
            var run = NewArenaRun();
            Assert.That(run.Sim.BigAttackTokenLimit, Is.EqualTo(Simulation.BigAttackTokensForArena(1)));
            Assert.That(run.Sim.BigAttackTokenLimit, Is.EqualTo(1));
            Assert.That(Simulation.BigAttackTokensForArena(5), Is.EqualTo(2));
        }

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


        // ---- родник ----

        private static ulong OfferHash(in RewardOffer offer)
        {
            ulong hash = Hashing.Offset;
            offer.HashInto(ref hash);
            return hash;
        }

        private static int SpringIndex(RiftRun run)
        {
            int found = -1;
            for (int i = 0; i < RiftRun.RewardChoices; i++)
                if (run.GetOffer(i).Kind == RewardKind.Spring)
                {
                    Assert.That(found, Is.EqualTo(-1), "два родника на экране");
                    found = i;
                }
            return found;
        }

        /// <summary>Ценность карточки из правила замены: стат, вещь, способность в полную панель, усиление, способность.</summary>
        private static int Value(RiftRun run, in RewardOffer offer)
            => offer.Kind == RewardKind.StatBoost ? 0 : offer.Kind == RewardKind.Item ? 1
                : offer.Kind == RewardKind.Ability ? (run.Loadout.IsFull ? 2 : 4) : offer.Kind == RewardKind.Talent ? 3 : 5;

        [TestCase(750, false)]
        [TestCase(749, true)]
        [TestCase(100, true)]
        public void Spring_AppearsOnlyBelowThreeQuartersHealth(int permille, bool offered)
        {
            var run = NewArenaRun();
            run.Sim.Entities.Health[0] = permille * run.Sim.Entities.MaxHealth[0] / 1000;
            ClearRiftAndReachExit(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.ChoosingReward));
            Assert.That(SpringIndex(run) >= 0, Is.EqualTo(offered));
        }

        [Test]
        public void Spring_ReplacesTheWeakestCard_AndLeavesTheRestOfTheSeedAlone()
        {
            for (ulong seed = 1; seed <= 12; seed++)
            {
                RiftRun full = NewRun(seed), hurt = NewRun(seed);
                hurt.Sim.Entities.Health[0] = hurt.Sim.Entities.MaxHealth[0] / 2;
                ClearRiftAndReachExit(full); ClearRiftAndReachExit(hurt);
                Assert.That(SpringIndex(full), Is.EqualTo(-1));
                int spring = SpringIndex(hurt);
                Assert.That(spring, Is.GreaterThanOrEqualTo(0), "сид " + seed);
                Assert.That(hurt.GetOffer(spring).HealPercent, Is.EqualTo(RiftRun.SpringHealPercent));
                int replaced = Value(full, full.GetOffer(spring));
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                {
                    if (i == spring) continue;
                    // Остальные карточки — те же, что у здорового героя: броски не сдвинуты.
                    Assert.That(OfferHash(hurt.GetOffer(i)), Is.EqualTo(OfferHash(full.GetOffer(i))), "сид " + seed);
                    int value = Value(full, full.GetOffer(i));
                    Assert.That(replaced, Is.LessThanOrEqualTo(value), "ушла не самая слабая карточка, сид " + seed);
                    if (value == replaced) Assert.That(i, Is.LessThan(spring), "при равной ценности уходит правая, сид " + seed);
                }
            }
        }

        [Test]
        public void Spring_HealsFortyPercent_UpToMaximum_AndTheNextArenaKeepsIt()
        {
            var run = NewArenaRun();
            EntityStore e = run.Sim.Entities;
            int max = e.MaxHealth[0];
            e.Health[0] = max / 10;
            ClearRiftAndReachExit(run);
            Assert.That(run.SpringHealAmount, Is.EqualTo(max * 40 / 100));
            int spring = SpringIndex(run);
            int taken = run.TakenRewardCount;
            Take(run, (RunCommand)((int)RunCommand.ChooseReward1 + spring));
            Assert.That(e.Health[0], Is.EqualTo(max / 10 + RiftRun.SpringHeal(max, RiftRun.SpringHealPercent)));
            Assert.That(run.TakenRewardCount, Is.EqualTo(taken + 1));
            Assert.That(run.GetTaken(taken).Kind, Is.EqualTo(RewardKind.Spring));
            int healed = e.Health[0];
            run.Step(Command(RunCommand.ChooseRoute1));
            Assert.That(run.Depth, Is.EqualTo(2));
            Assert.That(e.MaxHealth[0] - e.Health[0], Is.EqualTo(max - healed), "в следующую арену едет уже меньшая недостача");

            // Родник не лечит сверх максимума.
            e.Health[0] = max * 70 / 100;
            ClearRiftAndReachExit(run);
            Assert.That(run.SpringHealAmount, Is.EqualTo(max - max * 70 / 100));
            Take(run, (RunCommand)((int)RunCommand.ChooseReward1 + SpringIndex(run)));
            Assert.That(e.Health[0], Is.EqualTo(max));
        }

        [Test]
        public void Spring_NeverTakesTheBossReward()
        {
            var modules = PrototypeContent.Modules();
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var settings = new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian }) }, new[] { new EncounterPack(3, 100, new[] { guardian }) },
                new[] { new EncounterPack(4, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, elite: true) }) },
                1, 0, 100, Fix64.FromInt(5));
            var arena = new RiftLevelSettings(12, 1, 1, 2, 1, 2, 100, settings, playerHealth: 150, entryClearance: 14,
                solidEnvironment: true, naturalGlade: true).WithArenaSize(3);
            var boss = new RiftLevelSettings(20, 1, 1, 2, 1, 2, 100, settings, boss: true, playerHealth: 150,
                entryClearance: 14, solidEnvironment: true, naturalGlade: true).WithArenaSize(4);
            var location = new LocationDefinition(7, modules, new[] { arena, boss }, completeAtEnd: true);
            var run = new RiftRun(new Simulation(Seed, 512), modules, Items(), new[] { SwordId }, location: location);
            run.StartRun();
            ClearRiftAndReachExit(run);
            Take(run, NotSpring(run));
            run.Step(Command(RunCommand.ChooseRoute1));
            Assert.That(run.BossId, Is.GreaterThan(0));
            run.Sim.Entities.Health[0] = 1;
            ClearRiftAndReachExit(run);
            Assert.That(run.ChoosingArtifact, Is.True);
            Assert.That(SpringIndex(run), Is.EqualTo(-1), "после босса — только артефакты");
        }

        [Test]
        public void Spring_ReplaysDeterministically_AndIsPartOfTheHash()
        {
            RiftRun a = NewArenaRun(), b = NewArenaRun();
            a.Sim.Entities.Health[0] = b.Sim.Entities.Health[0] = 200;
            ClearRiftAndReachExit(a); ClearRiftAndReachExit(b);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            int spring = SpringIndex(a);
            Take(a, (RunCommand)((int)RunCommand.ChooseReward1 + spring));
            Take(b, (RunCommand)((int)RunCommand.ChooseReward1 + spring));
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            Assert.That(OfferHash(RewardOffer.OfSpring(40)), Is.Not.EqualTo(OfferHash(RewardOffer.OfSpring(30))));
            Assert.That(RewardOffer.OfSpring(40).HealPercent, Is.EqualTo(40));
            Assert.That(RewardOffer.OfArtifact(RunArtifact.SunSeal).HealPercent, Is.Zero);
        }
    }
}
