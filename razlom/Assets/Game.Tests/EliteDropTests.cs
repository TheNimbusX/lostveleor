using System;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Добыча с элит. Решения владельца от 15 сентября: каждая элита роняет ровно
    /// один предмет (65% вещь, 35% способность), он лежит на арене; вещь и
    /// способность при свободном слоте поднимаются сами; при полной панели
    /// способность ждёт выбора в мини-меню, бой идёт; не подобранное пропадает
    /// с ареной. Якорные способности стоят 20 / 25 / 15 / 30 лавидия.
    /// </summary>
    public class EliteDropTests
    {
        private static EncounterSettings Settings()
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var swarm = new EncounterGroup(EnemyKind.ForestRootSwarm, 3, 4, 30, growWithDepth: true);
            return new EncounterSettings(
                new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian, swarm }),
                    new EncounterPack(3, 100, new[] { swarm }), new EncounterPack(4, 100, new[] { guardian, guardian }) },
                new[] { new EncounterPack(5, 100, new[] { new EncounterGroup(EnemyKind.ForestGuardian, 2, 2) }) },
                new[] { new EncounterPack(6, 100, new[] {
                    new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, 240, 160, elite: true), swarm }) },
                3, 0, 100, Fix64.FromInt(4));
        }

        private static RiftRun NewRun(ulong seed, Action<RunLoadout> setup = null)
        {
            var modules = PrototypeContent.Modules();
            var level = new RiftLevelSettings(16, 1, 1, 2, 0, 0, 150, Settings());
            var location = new LocationDefinition(1, modules, new[] { level, level });
            var run = new RiftRun(new Simulation(seed, 512), modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
            run.StartRun();
            setup?.Invoke(run.Loadout);
            return run;
        }

        private static void FillSabre(RunLoadout loadout)
        {
            for (int slot = 1; slot < RunLoadout.Slots; slot++) loadout.Put(slot, slot);
        }

        private static int FirstElite(RiftRun run)
        {
            for (int i = 1; i < run.Sim.Entities.Count; i++)
                if (run.Encounters.IsElite(i) && i != run.BossId) return i;
            Assert.Fail("на арене нет элиты");
            return -1;
        }

        private static InputFrame Command(RunCommand command) => new InputFrame { Command = (byte)command };

        /// <summary>Убивает первую элиту и делает шаг, чтобы забег увидел смерть.</summary>
        private static int KillElite(RiftRun run)
        {
            int elite = FirstElite(run);
            run.Sim.Entities.Alive[elite] = false;
            run.Step(InputFrame.Empty);
            return elite;
        }

        private static RiftRun FindDrop(RewardKind kind, Action<RunLoadout> setup)
        {
            for (ulong seed = 1; seed < 400; seed++)
            {
                var run = NewRun(seed, setup);
                KillElite(run);
                if (run.DropCount == 1 && run.GetDrop(0).Offer.Kind == kind) return run;
            }
            Assert.Fail("не нашлось сида с дропом " + kind);
            return null;
        }

        private static void StandOn(RiftRun run, int drop)
        {
            run.Sim.Entities.Position[Simulation.PlayerId] = run.GetDrop(drop).Position;
            run.Step(InputFrame.Empty);
        }

        private static void StandAway(RiftRun run, int drop)
        {
            run.Sim.Entities.Position[Simulation.PlayerId] = run.GetDrop(drop).Position
                + new FixVec2(Fix64.FromInt(6), Fix64.Zero);
        }

        // ---- появление ----

        [Test]
        public void EliteDropsExactlyOneThing()
        {
            var run = NewRun(3);
            Assert.AreEqual(0, run.DropCount);

            KillElite(run);
            Assert.AreEqual(1, run.DropCount);

            for (int i = 0; i < 5; i++) run.Step(InputFrame.Empty);
            Assert.AreEqual(1, run.DropCount, "одна элита уронила дважды");
        }

        [Test]
        public void EliteDropsAreSixtyFiveItemsToThirtyFiveAbilities()
        {
            const int runs = 300;
            int items = 0;
            for (ulong seed = 1; seed <= runs; seed++)
            {
                var run = NewRun(seed);
                KillElite(run);
                Assert.AreEqual(1, run.DropCount, $"сид {seed}");
                RewardKind kind = run.GetDrop(0).Offer.Kind;
                Assert.IsTrue(kind == RewardKind.Item || kind == RewardKind.Ability, $"сид {seed}: {kind}");
                if (kind == RewardKind.Item) items++;
            }
            Assert.AreEqual(.65f, items / (float)runs, .08f);
        }

        // ---- подбор ----

        [Test]
        public void ItemIsPickedUpOnceByWalkingOverIt()
        {
            var run = FindDrop(RewardKind.Item, null);
            int taken = run.TakenRewardCount;

            StandOn(run, 0);
            Assert.IsTrue(run.GetDrop(0).Claimed);
            Assert.AreEqual(taken + 1, run.TakenRewardCount);
            Assert.AreEqual(RewardKind.Item, run.GetTaken(run.TakenRewardCount - 1).Kind);

            run.Step(InputFrame.Empty);
            Assert.AreEqual(taken + 1, run.TakenRewardCount, "вещь подобрана дважды");
        }

        [Test]
        public void AbilityWithAFullPanelWaitsOnTheGround()
        {
            var run = FindDrop(RewardKind.Ability, FillSabre);
            int pool = run.GetDrop(0).Offer.PoolIndex;

            StandOn(run, 0);
            for (int i = 0; i < 10; i++) run.Step(InputFrame.Empty);

            Assert.IsFalse(run.GetDrop(0).Claimed, "способность поднялась без выбора");
            Assert.IsFalse(run.Loadout.Owns(pool));
            Assert.AreEqual(0, run.NearestAbilityDrop(RiftRun.DropMenuRadius));
            Assert.IsTrue(run.Phase == RunPhase.Clearing || run.Phase == RunPhase.SeekingExit, "бой остановился");
        }

        [Test]
        public void ReplacingFromTheMenuSwapsTheSlotAndDropsItsTalents()
        {
            var run = FindDrop(RewardKind.Ability, FillSabre);
            int pool = run.GetDrop(0).Offer.PoolIndex;
            run.Loadout.TakeTalent(1);
            run.Loadout.TakeTalent(1);
            StandOn(run, 0);

            run.Step(Command(RunCommand.PickupReplaceSlot2));

            Assert.IsTrue(run.GetDrop(0).Claimed);
            Assert.AreEqual(pool, run.Loadout.PoolIndexAt(1));
            Assert.AreEqual(0, run.Loadout.TalentRank(1), "таланты заменённой способности остались");
            Assert.AreEqual(PelagKit.PoolDefinition(pool).Id, run.Sim.GetAbility(1).DefinitionId);
        }

        [Test]
        public void UnclaimedDropsStayBehindWithTheArena()
        {
            var run = NewRun(5);
            KillElite(run);
            Assert.AreEqual(1, run.DropCount);

            for (int e = 0; e < run.Encounters.Count; e++)
            {
                var encounter = run.Encounters.Get(e);
                if (encounter.Role == EncounterRole.RewardBranch) continue;
                for (int i = 0; i < encounter.EnemyCount; i++) run.Sim.Entities.Alive[encounter.FirstEntity + i] = false;
            }
            run.Step(InputFrame.Empty);
            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward, run.Phase);
            run.Step(Command(RunCommand.ChooseReward1));
            if (run.Phase == RunPhase.ReplacingAbility) run.Step(Command(RunCommand.SalvageAbility));

            Assert.AreEqual(2, run.Depth);
            Assert.AreEqual(0, run.DropCount, "добыча прошлой арены переехала в новую");
        }

        // ---- цены якоря ----

    }
}
