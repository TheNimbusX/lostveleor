using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Клятвы и грани сердца в бою (решение 06.10, план «Лагерь 06–10.10», T2; спека забега §8.4).
    ///
    /// Снимок RunBoons закрепляется до старта забега и уходит в симуляцию один раз. Статовые
    /// клятвы — модификаторы листа героя и переживают смену арены; долгое состояние («Последний
    /// вдох», переброс, бутон) живёт весь забег и сбрасывается новым; без клятв хеш прежний.
    /// </summary>
    public sealed class RunBoonsTests
    {
        private static InputFrame Command(RunCommand command) => new InputFrame { Command = (byte)command };

        private static RunBoons Oath(OathId id, int rank) => RunBoons.Empty.WithRank(id, rank);

        /// <summary>Тестовая арена без врагов, эталонный герой.</summary>
        private static Simulation Arena(ulong seed = 4242, int enemies = 0)
        {
            var sim = new Simulation(seed, 64);
            sim.ApplyHeroBaseline();
            sim.SetupTestArena(enemies);
            for (int id = 1; id < sim.Entities.Count; id++) sim.Entities.NextAttackTick[id] = int.MaxValue;
            return sim;
        }

        /// <summary>Неподвижная мишень с бегом (ходьба видна по MoveStep), которая не бьёт.</summary>
        private static int Dummy(Simulation sim, FixVec2 at, int health = 1000000)
        {
            int id = sim.Entities.Spawn(at, health, Faction.Orvill);
            var sheet = sim.Entities.Stats[id];
            sheet.SetBase(StatType.MaxHealth, Fix64.FromInt(health));
            sheet.SetBase(StatType.MoveSpeed, Fix64.FromInt(3));
            sim.Entities.RefreshStats(id);
            sim.Entities.Health[id] = health;
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            sim.Grid.Rebuild(sim.Entities);
            return id;
        }

        private static FixVec2 At(int x, int y) => new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y));

        /// <summary>Поджечь насмерть от имени героя: смерть идёт штатным путём шага (событие Death).</summary>
        private static void Burn(Simulation sim, int id)
            => sim.Statuses.ApplyBurn(id, Fix64.FromInt(10000000), 1, Simulation.PlayerId, -1);

        // ---- статы героя ----

        [Test]
        public void HeroOaths_AddStatsPerRank()
        {
            Simulation sim = Arena();
            EntityStore e = sim.Entities;
            int health = e.MaxHealth[0], lavidium = e.MaxLavidium[0];
            Fix64 move = e.Stats[0].Get(StatType.MoveSpeed), crit = e.Stats[0].Get(StatType.CritChance);
            for (int rank = 1; rank <= 3; rank++)
            {
                sim.SetBoons(Oath(OathId.ToughHide, rank).WithRank(OathId.LightStep, rank)
                    .WithRank(OathId.KeenEye, rank).WithRank(OathId.DeepReserve, rank));
                Assert.AreEqual(health + 30 * rank, e.MaxHealth[0], "шкура +30 за ступень");
                Assert.AreEqual(lavidium + 20 * rank, e.MaxLavidium[0], "запас +20 за ступень");
                Assert.AreEqual(crit + Fix64.Ratio(4 * rank, 100), e.Stats[0].Get(StatType.CritChance), "глаз +4% крита");
                Fix64 expected = move * (Fix64.One + Fix64.Ratio(5 * rank, 100));
                Assert.Less(Fix64.Abs(expected - e.Stats[0].Get(StatType.MoveSpeed)).Raw, Fix64.Ratio(1, 1000).Raw, "шаг +5% бега");
            }

            // Через забег: расстановка арены стирает лист, клятвы встают обратно, полное здоровье — с ними.
            RiftRun plain = FormBaselineScenarios.NewArenaRun(31), oath = FormBaselineScenarios.NewArenaRun(31);
            Assert.IsTrue(oath.SetBoons(Oath(OathId.ToughHide, 2)));
            plain.StartRun();
            oath.StartRun();
            plain.Step(InputFrame.Empty);
            oath.Step(InputFrame.Empty);
            Assert.AreEqual(plain.Sim.Entities.MaxHealth[0] + 60, oath.Sim.Entities.MaxHealth[0]);
            Assert.AreEqual(oath.Sim.Entities.MaxHealth[0], oath.Sim.Entities.Health[0], "забег — с полным здоровьем вместе с клятвой");

            // Вторая арена: недостача переезжает, максимум — с клятвой. Оба забега идут одними
            // шагами: награда арены может сама менять здоровье, сравнивается только клятва.
            foreach (RiftRun run in new[] { plain, oath })
            {
                run.Sim.Entities.Health[0] = run.Sim.Entities.MaxHealth[0] - 50;
                Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                run.Step(FormBaselineScenarios.Choice(0));
                if (run.Phase == RunPhase.ReplacingAbility) run.Step(Command(RunCommand.ReplaceSlot2));
                run.Step(Command(RunCommand.ChooseRoute1));
                Assert.AreEqual(RunPhase.Clearing, run.Phase);
                run.Step(InputFrame.Empty);
            }
            EntityStore before = plain.Sim.Entities, after = oath.Sim.Entities;
            Assert.AreEqual(before.MaxHealth[0] + 60, after.MaxHealth[0], "клятва пережила расстановку");
            Assert.AreEqual(before.MaxHealth[0] - before.Health[0], after.MaxHealth[0] - after.Health[0], "недостача та же");
        }

        [Test]
        public void HeavyHand_ScalesSabreHitsOnly()
        {
            List<int> plainHits = SabreHits(RunBoons.Empty, out int plainAbility);
            List<int> heavyHits = SabreHits(Oath(OathId.HeavyHand, 3), out int heavyAbility);
            Assert.Greater(plainHits.Count, 2, "серия сабли попала");
            Assert.AreEqual(plainHits.Count, heavyHits.Count);
            for (int i = 0; i < plainHits.Count; i++)
                Assert.AreEqual(System.Math.Max(1, CombatStats.RoundToInt(Fix64.FromInt(plainHits[i]) * Fix64.Ratio(124, 100))),
                    heavyHits[i], "удар " + i + ": +24% на третьей ступени");
            Assert.AreEqual(plainAbility, heavyAbility, "способности рука не касается");
        }

        private static List<int> SabreHits(RunBoons boons, out int ability)
        {
            Simulation sim = Arena(77);
            EntityStore e = sim.Entities;
            e.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            sim.RefreshPlayerStats(false);
            sim.SetBoons(boons);
            sim.PlayerInvulnerable = true;
            int target = Dummy(sim, new FixVec2(Fix64.Ratio(3, 2), Fix64.Zero));
            e.Stats[target].SetBase(StatType.MoveSpeed, Fix64.Zero);
            e.RefreshStats(target);
            var hits = new List<int>();
            var hold = InputFrame.Empty;
            hold.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            hold.Flags = (byte)(InputFlags.Attack | InputFlags.AttackPressed);
            for (int t = 0; t < 90; t++)
            {
                sim.Step(in hold);
                hold.Flags = (byte)InputFlags.Attack;
                foreach (SimEvent ev in sim.Events)
                    if (ev.Type == SimEventType.Damage && ev.Source == 0 && ev.Target == target
                        && ev.DamageOrigin == DamageOrigin.BasicAttack) hits.Add(ev.Amount);
            }
            sim.ApplyAbilityDamage(0, target, 100, 0, DamageType.Physical);
            ability = 0;
            foreach (SimEvent ev in sim.Events)
                if (ev.Type == SimEventType.Damage && ev.DamageOrigin == DamageOrigin.Ability) ability = ev.Amount;
            Assert.Greater(ability, 0);
            return hits;
        }

        [Test]
        public void QuickRoll_DashCooldown_45_40_36_31()
        {
            Simulation sim = Arena();
            new RunLoadout().ApplyTo(sim);
            AbilityBuild dash = sim.GetAbility(PelagKit.DashSlot), whirlwind = sim.GetAbility(0);
            Assert.AreEqual(AbilityDefinition.DashId, dash.DefinitionId);
            int other = sim.AbilityCooldownTicks(whirlwind);
            int[] expected = { 45, 40, 36, 31 };
            for (int rank = 0; rank <= 3; rank++)
            {
                sim.SetBoons(rank == 0 ? RunBoons.Empty : Oath(OathId.QuickRoll, rank));
                Assert.AreEqual(expected[rank], sim.AbilityCooldownTicks(dash), "ступень " + rank);
                Assert.AreEqual(other, sim.AbilityCooldownTicks(whirlwind), "кувырок режет только рывок");
            }
        }

        // ---- выживание ----

        [Test]
        public void Steadfast_CutsEliteAndBossDamage_BothPaths()
        {
            // Путь способности: элита режется, обычный враг — нет.
            int[] plain = AbilityHits(RunBoons.Empty), steady = AbilityHits(Oath(OathId.Steadfast, 1));
            Assert.AreEqual(System.Math.Max(1, CombatStats.RoundToInt(Fix64.FromInt(plain[0]) * Fix64.Ratio(90, 100))), steady[0], "элита −10%");
            Assert.Less(steady[0], plain[0]);
            Assert.AreEqual(plain[1], steady[1], "обычный враг не режется");

            // Путь удара врага (ApplyAttack): элита бьёт героя вплотную.
            int plainSwing = EliteSwing(RunBoons.Empty), steadySwing = EliteSwing(Oath(OathId.Steadfast, 1));
            Assert.Greater(plainSwing, 0, "элита ударила");
            Assert.AreEqual(System.Math.Max(1, CombatStats.RoundToInt(Fix64.FromInt(plainSwing) * Fix64.Ratio(90, 100))), steadySwing);
        }

        private static int[] AbilityHits(RunBoons boons)
        {
            Simulation sim = Arena(5, enemies: 2);
            sim.MarkElite(1);
            sim.SetBoons(boons);
            var hits = new int[2];
            for (int source = 1; source <= 2; source++)
            {
                int before = sim.Entities.Health[0];
                sim.ApplyAbilityDamage(source, 0, 100, -1, DamageType.Physical);
                hits[source - 1] = before - sim.Entities.Health[0];
            }
            return hits;
        }

        private static int EliteSwing(RunBoons boons)
        {
            Simulation sim = new Simulation(19, 64);
            sim.ApplyHeroBaseline();
            sim.SetupTestArena(1);
            sim.MarkElite(1);
            sim.SetBoons(boons);
            sim.Entities.Position[1] = new FixVec2(Fix64.FromInt(1), Fix64.Zero);
            for (int t = 0; t < 600; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (SimEvent ev in sim.Events)
                    if (ev.Type == SimEventType.Damage && ev.Source == 1 && ev.Target == 0) return ev.Amount;
            }
            return 0;
        }

        [Test]
        public void LastBreath_RevivesOnceAtThirtyPercent_AfterGuardianVow()
        {
            Simulation sim = Arena(enemies: 1);
            sim.SetArtifact(RunArtifact.GuardianVow);
            sim.SetBoons(Oath(OathId.LastBreath, 1));
            EntityStore e = sim.Entities;
            int max = e.MaxHealth[0];

            sim.ApplyAbilityDamage(1, 0, 10000000, -1, DamageType.Physical);
            Assert.IsTrue(e.Alive[0]);
            Assert.AreEqual(max / 2, e.Health[0], "первым спасает Обет Хранителя");
            Assert.IsFalse(sim.LastBreathUsed);
            for (int t = 0; t <= Simulation.VowImmuneTicks; t++) sim.Step(InputFrame.Empty);

            sim.ApplyAbilityDamage(1, 0, 10000000, -1, DamageType.Physical);
            Assert.IsTrue(e.Alive[0], "«Последний вдох» поднял");
            Assert.AreEqual(max * 30 / 100, e.Health[0]);
            Assert.IsTrue(sim.LastBreathUsed);
            sim.ApplyAbilityDamage(1, 0, 10000000, -1, DamageType.Physical);
            Assert.AreEqual(max * 30 / 100, e.Health[0], "секунда неуязвимости после подъёма");

            for (int t = 0; t < Simulation.LastBreathImmuneTicks; t++) sim.Step(InputFrame.Empty);
            sim.ApplyAbilityDamage(1, 0, 10000000, -1, DamageType.Physical);
            Assert.IsFalse(e.Alive[0], "раз за забег");
        }

        [Test]
        public void EnemyBlood_EliteKillHealsFivePercent()
        {
            Simulation sim = Arena(enemies: 2);
            sim.MarkElite(1);
            sim.SetBoons(Oath(OathId.EnemyBlood, 1));
            EntityStore e = sim.Entities;
            int max = e.MaxHealth[0];
            e.Health[0] = max / 2;

            Burn(sim, 2);
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(e.Alive[2]);
            Assert.AreEqual(max / 2, e.Health[0], "обычный враг не лечит");

            Burn(sim, 1);
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(e.Alive[1]);
            Assert.AreEqual(max / 2 + max * 5 / 100, e.Health[0], "элита — 5% максимума");
        }

        [Test]
        public void GenerousSpring_OffersFortySixPercent()
        {
            RiftRun plain = FormBaselineScenarios.NewArenaRun(12), spring = FormBaselineScenarios.NewArenaRun(12);
            Assert.IsTrue(spring.SetBoons(Oath(OathId.GenerousSpring, 1)));
            plain.StartRun();
            spring.StartRun();
            plain.Step(InputFrame.Empty);
            spring.Step(InputFrame.Empty);
            int max = spring.Sim.Entities.MaxHealth[0];
            plain.Sim.Entities.Health[0] = spring.Sim.Entities.Health[0] = 1;
            Assert.AreEqual(RiftRun.SpringHeal(max, 40), plain.SpringHealAmount);
            Assert.AreEqual(RiftRun.SpringHeal(max, 46), spring.SpringHealAmount, "+15% от самого лечения: 40 → 46");
        }

        // ---- удача забега ----

        [Test]
        public void SecondLook_OneFreeReroll_GiftSpentFirst()
        {
            RiftRun run = FormBaselineScenarios.NewArenaRun(21);
            run.SetPreparation(new RunPreparation(AbilityDefinition.WhirlwindId, CampGift.BackupPlan,
                PotionKind.SmallHealth, PotionKind.SmallLavidium));
            Assert.IsTrue(run.SetBoons(Oath(OathId.SecondLook, 1)));
            run.StartRun();
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
            Assert.IsTrue(run.CanFreeReroll);
            Assert.IsTrue(run.CanRerollReward);

            ulong screen = run.Hash();
            run.Step(Command(RunCommand.RerollReward));
            Assert.IsTrue(run.Sim.SecondLookUsed, "бесплатный — первым");
            Assert.IsFalse(run.GiftRerollUsed, "дар остался");
            Assert.IsFalse(run.CanFreeReroll);
            Assert.AreNotEqual(screen, run.Hash());

            run.Step(Command(RunCommand.RerollReward));
            Assert.IsTrue(run.GiftRerollUsed, "потом дар");
            ulong spent = run.Hash();
            run.Step(Command(RunCommand.RerollReward));
            Assert.AreEqual(spent, run.Hash(), "третьего переброса нет");

            // Без дара клятва перебрасывает сама.
            RiftRun bare = FormBaselineScenarios.NewArenaRun(21);
            Assert.IsTrue(bare.SetBoons(Oath(OathId.SecondLook, 1)));
            bare.StartRun();
            Assert.IsTrue(FormBaselineScenarios.ClearToReward(bare));
            Assert.IsFalse(bare.CanRerollReward);
            ulong before = bare.Hash();
            bare.Step(Command(RunCommand.RerollReward));
            Assert.AreNotEqual(before, bare.Hash());
            Assert.IsFalse(bare.CanFreeReroll);
        }

        // ---- грани сердца ----

        [Test]
        public void HeartRoots_DashRootsNearbyNonBossForFifteenTicks()
        {
            Simulation sim = Arena();
            new RunLoadout().ApplyTo(sim);
            sim.SetBoons(RunBoons.Empty.WithFacet(HeartFacet.ThicketRoots));
            sim.PlayerInvulnerable = true;
            int near = Dummy(sim, At(0, 2)), far = Dummy(sim, At(0, 7)), boss = Dummy(sim, At(0, -2));
            sim.Entities.Kind[boss] = EnemyKind.ForestThicketMaster;
            sim.Step(InputFrame.Empty);
            Fix64 step = sim.Entities.MoveStep[near];
            Assert.Greater(step.Raw, 0L);

            var dash = InputFrame.Empty;
            dash.AbilityMask = (byte)(1 << PelagKit.DashSlot);
            dash.Aim = At(-4, 0);
            sim.Step(in dash);
            Assert.AreEqual(AbilityDefinition.DashId, sim.PlayerAction.DefinitionId);
            Assert.Greater(sim.HeartRootTicksLeft(near), 0, "в 3 м от начала рывка");
            Assert.AreEqual(0, sim.HeartRootTicksLeft(far), "дальше 3 м — свободен");
            Assert.AreEqual(0, sim.HeartRootTicksLeft(boss), "босс не связывается");

            int rooted = 0;
            for (int t = 0; t < 40; t++)
            {
                sim.Step(InputFrame.Empty);
                if (sim.Entities.MoveStep[near].Raw == 0) rooted++;
                Assert.Greater(sim.Entities.MoveStep[far].Raw, 0L);
                Assert.Greater(sim.Entities.MoveStep[boss].Raw, 0L);
            }
            Assert.AreEqual(Simulation.HeartRootTicks, rooted, "ходьба 0 ровно 15 тиков");
            Assert.AreEqual(step, sim.Entities.MoveStep[near], "корни сняты");
        }

        [Test]
        public void HeartPollen_KillLeavesSlowingCloudThreeSeconds()
        {
            Simulation sim = Arena();
            sim.SetBoons(RunBoons.Empty.WithFacet(HeartFacet.ThicketPollen));
            sim.PlayerInvulnerable = true;
            int victim = Dummy(sim, At(8, 0), 10), neighbour = Dummy(sim, At(9, 0)), far = Dummy(sim, At(-12, 0));
            sim.MarkElite(neighbour);
            Fix64 speed = sim.Entities.Stats[neighbour].Get(StatType.MoveSpeed);

            Burn(sim, victim);
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.Entities.Alive[victim]);
            Assert.IsTrue(sim.TryGetPollenCloud(0, out FixVec2 cloud, out int left));
            Assert.AreEqual(At(8, 0).X, cloud.X);
            Assert.AreEqual(At(8, 0).Y, cloud.Y);
            Assert.Greater(left, Simulation.PollenTicks - 3);
            Assert.IsTrue(sim.PollenSlowed(neighbour), "в облаке — и элита");
            Assert.IsFalse(sim.PollenSlowed(far));
            sim.Step(InputFrame.Empty);
            Fix64 slowed = speed * Fix64.Ratio(70, 100);
            Assert.Less(Fix64.Abs(slowed - sim.Entities.Stats[neighbour].Get(StatType.MoveSpeed)).Raw, Fix64.Ratio(1, 1000).Raw, "−30% бега");

            for (int t = 0; t < Simulation.PollenTicks + Simulation.PollenLingerTicks + 2; t++) sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetPollenCloud(0, out _, out _), "облако живёт 3 с");
            Assert.IsFalse(sim.PollenSlowed(neighbour));
            Assert.AreEqual(speed, sim.Entities.Stats[neighbour].Get(StatType.MoveSpeed));

            // Больше восьми облаков разом не бывает: девятое вытесняет старое.
            var victims = new int[Simulation.MaxPollenClouds + 1];
            for (int i = 0; i < victims.Length; i++) victims[i] = Dummy(sim, At(-4 + i, 6), 10);
            foreach (int id in victims) Burn(sim, id);
            sim.Step(InputFrame.Empty);
            int live = 0;
            for (int i = 0; i < Simulation.MaxPollenClouds; i++) if (sim.TryGetPollenCloud(i, out _, out _)) live++;
            Assert.AreEqual(Simulation.MaxPollenClouds, live);
        }

        [Test]
        public void HeartBloom_HealsFivePercentEveryTwentySecondsWhenHurt()
        {
            Simulation sim = Arena();
            sim.SetBoons(RunBoons.Empty.WithFacet(HeartFacet.ThicketBloom));
            EntityStore e = sim.Entities;
            int max = e.MaxHealth[0], hurt = max / 2;
            e.Health[0] = hurt;
            for (int t = 0; t < Simulation.BloomPeriodTicks; t++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(hurt, e.Health[0], "бутон зреет 20 с");
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(hurt + max * 5 / 100, e.Health[0], "5% максимума");

            // При полном здоровье зрелый бутон ждёт раны и лечит сразу, как она появилась.
            e.Health[0] = max;
            for (int t = 0; t < Simulation.BloomPeriodTicks + 30; t++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(0, sim.BloomTicksLeft, "созрел и ждёт");
            e.Health[0] = hurt;
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(hurt + max * 5 / 100, e.Health[0]);
            Assert.AreEqual(Simulation.BloomPeriodTicks, sim.BloomTicksLeft + 1, "следующий — через 20 с");
        }

        // ---- состояние забега и хеш ----

        [Test]
        public void RunState_SurvivesArenaChange_ResetsOnNewRun()
        {
            RiftRun run = FormBaselineScenarios.NewArenaRun(44);
            Assert.IsTrue(run.SetBoons(Oath(OathId.LastBreath, 1).WithFacet(HeartFacet.ThicketBloom)));
            run.StartRun();
            run.Step(InputFrame.Empty);
            Simulation sim = run.Sim;
            int bloomAt = sim.Tick + sim.BloomTicksLeft;
            int enemy = -1;
            for (int id = 1; id < sim.Entities.Count && enemy < 0; id++)
                if (sim.Entities.Alive[id] && sim.Entities.Side[id] != Faction.Wole) enemy = id;
            Assert.Greater(enemy, 0);
            sim.ApplyAbilityDamage(enemy, 0, 10000000, -1, DamageType.Physical);
            Assert.IsTrue(sim.LastBreathUsed);

            Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
            run.Step(FormBaselineScenarios.Choice(0));
            if (run.Phase == RunPhase.ReplacingAbility) run.Step(Command(RunCommand.ReplaceSlot2));
            run.Step(Command(RunCommand.ChooseRoute1));
            Assert.AreEqual(2, run.Depth);
            run.Step(InputFrame.Empty);
            Assert.IsTrue(sim.LastBreathUsed, "вдох потрачен на весь забег");
            Assert.AreEqual(bloomAt, sim.Tick + sim.BloomTicksLeft, "бутон зреет сквозь арены");

            run.StartRun();
            Assert.IsFalse(sim.LastBreathUsed, "новый забег — вдох снова готов");
            Assert.AreEqual(Simulation.BloomPeriodTicks, sim.BloomTicksLeft, "и бутон заново");
        }

        [Test]
        public void EmptyBoons_LeaveStateHashUnchanged()
        {
            Simulation plain = Arena(91, enemies: 3), empty = Arena(91, enemies: 3), reset = Arena(91, enemies: 3);
            empty.SetBoons(RunBoons.Empty);
            reset.SetBoons(Oath(OathId.ToughHide, 3).WithFacet(HeartFacet.ThicketPollen));
            Assert.AreNotEqual(plain.StateHash(), reset.StateHash());
            reset.SetBoons(RunBoons.Empty);
            Assert.AreEqual(plain.StateHash(), empty.StateHash());
            Assert.AreEqual(plain.StateHash(), reset.StateHash(), "сброс в пустой снимок — как без клятв");
            var input = InputFrame.Empty;
            input.Aim = At(4, 3);
            input.Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack);
            for (int t = 0; t < 120; t++)
            {
                plain.Step(in input);
                empty.Step(in input);
                reset.Step(in input);
                Assert.AreEqual(plain.StateHash(), empty.StateHash(), "тик " + t);
                Assert.AreEqual(plain.StateHash(), reset.StateHash(), "тик " + t);
            }
        }

        [Test]
        public void SetBoons_RejectsStartedRunAndInvalidRanks()
        {
            RiftRun run = FormBaselineScenarios.NewArenaRun(3);
            Assert.IsFalse(run.SetBoons(Oath(OathId.LastBreath, 2)), "у не-героической клятвы одна ступень");
            Assert.IsFalse(run.SetBoons(RunBoons.Empty.WithFacet((HeartFacet)7)), "неизвестная грань");
            Assert.IsTrue(run.Boons.IsEmpty);
            Assert.AreEqual(3, Oath(OathId.ToughHide, 9).Rank(OathId.ToughHide), "ступень зажимается в два бита");
            Assert.IsTrue(run.SetBoons(Oath(OathId.ToughHide, 3)));
            run.StartRun();
            Assert.IsFalse(run.SetBoons(Oath(OathId.ToughHide, 1)), "забег начат");
            Assert.AreEqual(3, run.Boons.Rank(OathId.ToughHide));

            Assert.AreEqual(3, RunBoons.MaxRank(OathId.QuickRoll));
            Assert.AreEqual(1, RunBoons.MaxRank(OathId.LastBreath));
            Assert.AreEqual(1, RunBoons.MaxRank(OathId.RuneSage));
            Assert.AreEqual(0, RunBoons.MaxRank(OathId.None));
        }

        [Test]
        public void SameSeedSameBoons_SameHashes()
        {
            RunBoons all = RunBoons.Empty;
            for (int id = 1; id <= OathIds.Count; id++) all = all.WithRank((OathId)id, RunBoons.MaxRank((OathId)id));
            all = all.WithFacet(HeartFacet.ThicketRoots).WithFacet(HeartFacet.ThicketPollen).WithFacet(HeartFacet.ThicketBloom);
            RiftRun a = FormBaselineScenarios.NewArenaRun(57), b = FormBaselineScenarios.NewArenaRun(57),
                plain = FormBaselineScenarios.NewArenaRun(57);
            Assert.IsTrue(a.SetBoons(all));
            Assert.IsTrue(b.SetBoons(all));
            a.StartRun();
            b.StartRun();
            plain.StartRun();
            var input = InputFrame.Empty;
            input.Aim = At(5, 2);
            input.Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack);
            for (int t = 0; t < 240; t++)
            {
                input.AbilityMask = t % 50 == 10 ? (byte)(1 << PelagKit.DashSlot) : t % 40 == 0 ? (byte)1 : (byte)0;
                a.Step(in input);
                b.Step(in input);
                plain.Step(in input);
                Assert.AreEqual(a.Sim.StateHash(), b.Sim.StateHash(), "тик " + t);
                Assert.AreEqual(a.Hash(), b.Hash(), "тик " + t);
            }
            Assert.AreNotEqual(plain.Sim.StateHash(), a.Sim.StateHash(), "клятвы в хеше");
        }
    }
}
