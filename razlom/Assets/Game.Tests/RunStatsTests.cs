using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Статистика забега для экрана итогов (этап 4, п. 11): время, убийства по
    /// видам, урон, лучший удар, криты, уклонения, зелья, способности, уровни.
    ///
    /// Счёт — дело сессии, а не боя: он только читает события симуляции и
    /// ничего в ней не меняет. Поэтому здесь две половины: разбор событий на
    /// собранном вручную списке (точные числа) и настоящий короткий забег через
    /// GameSession (проводка: какие тики считаются, экран награды не считается
    /// дважды, каждый забег с нуля).
    /// </summary>
    public class RunStatsTests
    {
        private const int Hero = Simulation.PlayerId;
        private const ulong Seed = 0x57A75UL;
        private static readonly FixVec2 At = FixVec2.Zero;

        // ---- разбор событий ----

        private static Simulation Arena()
        {
            var sim = new Simulation(1234, 64);
            sim.SetupTestArena(0);
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            return sim;
        }

        private static int Foe(Simulation sim, EnemyKind kind)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.FromInt(3 + sim.Entities.Count), Fix64.Zero), 100, Faction.Orvill);
            sim.Entities.Kind[id] = kind;
            return id;
        }

        [Test]
        public void Record_TalliesDamageCritsBestHitEvadesAndCasts()
        {
            Simulation sim = Arena();
            int guardian = Foe(sim, EnemyKind.ForestGuardian);
            int wendigo = Foe(sim, EnemyKind.ForestWendigo);
            sim.MarkElite(wendigo);

            var events = new List<SimEvent>
            {
                SimEvent.Damage(Hero, guardian, 40, false, At, DamageType.Physical),
                SimEvent.Damage(Hero, guardian, 90, true, At, DamageType.Physical),
                SimEvent.Damage(Hero, wendigo, 150, false, At, DamageType.Physical, DamageOrigin.Ability, 0),
                // Равный удар позже лучшим не становится.
                SimEvent.Damage(Hero, guardian, 150, true, At, DamageType.Physical),
                SimEvent.DamageOverTime(Hero, wendigo, 7, At, DamageType.Fire),
                SimEvent.DamageOverTime(Hero, wendigo, 7, At, DamageType.Fire),
                SimEvent.DamageOverTime(Hero, guardian, 7, At, DamageType.Fire),
                SimEvent.Damage(guardian, Hero, 30, false, At, DamageType.Physical),
                SimEvent.DamageOverTime(wendigo, Hero, 4, At, DamageType.Fire),
                // Враг по врагу — не урон героя.
                SimEvent.Damage(guardian, wendigo, 500, true, At, DamageType.Physical),
                new SimEvent(SimEventType.Evaded, Hero, -1, 0, false, At),
                SimEvent.Cast(Hero, 0, At),
                SimEvent.Cast(guardian, 0, At),
            };

            var stats = new RunStats();
            ulong hash = sim.StateHash();
            stats.Record(events, sim, bossId: -1);

            Assert.AreEqual(hash, sim.StateHash(), "статистика только читает бой");
            Assert.AreEqual(4, stats.Hits);
            Assert.AreEqual(2, stats.Crits);
            Assert.AreEqual(430, stats.DirectDamageDealt);
            Assert.AreEqual(21, stats.DamageOverTimeDealt);
            Assert.AreEqual(451, stats.DamageDealt);
            Assert.AreEqual(34, stats.DamageTaken);
            Assert.AreEqual(1, stats.Evades);
            Assert.AreEqual(1, stats.AbilitiesCast, "каст врага не в счёт");

            Assert.AreEqual(150, stats.BestHit);
            Assert.AreEqual(EnemyKind.ForestWendigo, stats.BestHitTarget, "при равенстве остаётся первый");
            Assert.AreEqual(RunFoeRank.Elite, stats.BestHitTargetRank);
            Assert.IsFalse(stats.BestHitCrit);
            Assert.AreEqual(DamageOrigin.Ability, stats.BestHitOrigin);
            Assert.AreEqual(AbilityDefinition.WhirlwindId, stats.BestHitAbility);
            Assert.AreEqual(0, stats.Kills);
        }

        [Test]
        public void Record_CountsKillsPerKindWithElitesAndBoss()
        {
            Simulation sim = Arena();
            int boss = Foe(sim, EnemyKind.ForestGuardian);
            int elite = Foe(sim, EnemyKind.ForestWendigo);
            int swarmA = Foe(sim, EnemyKind.ForestRootSwarm);
            int swarmB = Foe(sim, EnemyKind.ForestRootSwarm);
            int splitling = Foe(sim, EnemyKind.ForestSplitling);
            int burrower = Foe(sim, EnemyKind.ForestRootSnarer);
            // Босса расстановка помечает и элитой (SetupBossArena): ранг — босс.
            sim.MarkElite(boss);
            sim.MarkElite(elite);

            var events = new List<SimEvent>
            {
                SimEvent.Death(Hero, swarmA, At),
                SimEvent.Death(Hero, elite, At),
                SimEvent.Death(Hero, swarmB, At),
                SimEvent.Death(Hero, boss, At),
                // Не герой убил — не его убийство; ушедший в землю — не убит.
                SimEvent.Death(-1, splitling, At),
                SimEvent.Burrow(burrower, At),
            };

            var stats = new RunStats();
            stats.Record(events, sim, bossId: boss);

            Assert.AreEqual(4, stats.Kills);
            Assert.AreEqual(1, stats.BossKills);
            Assert.AreEqual(1, stats.EliteKills, "босс не считается элитой дважды");
            Assert.AreEqual(2, stats.KillsOf(EnemyKind.ForestRootSwarm));
            Assert.AreEqual(1, stats.KillsOf(EnemyKind.ForestWendigo));
            Assert.AreEqual(1, stats.KillsOf(EnemyKind.ForestGuardian), "босс идёт и в свой вид");
            Assert.AreEqual(0, stats.KillsOf(EnemyKind.ForestSplitling));
            Assert.AreEqual(0, stats.KillsOf(EnemyKind.ForestRootSnarer));

            Assert.AreEqual(3, stats.KilledKindCount);
            Assert.AreEqual(EnemyKind.ForestRootSwarm, stats.KilledKind(0), "виды — в порядке первого убийства");
            Assert.AreEqual(EnemyKind.ForestWendigo, stats.KilledKind(1));
            Assert.AreEqual(EnemyKind.ForestGuardian, stats.KilledKind(2));
            Assert.AreEqual(EnemyKind.None, stats.KilledBy, "герой жив");
        }

        [Test]
        public void Record_HeroDeathRemembersTheKiller()
        {
            Simulation sim = Arena();
            int boss = Foe(sim, EnemyKind.ForestGuardian);

            var slain = new RunStats();
            slain.Record(new List<SimEvent> { SimEvent.Death(boss, Hero, At) }, sim, bossId: boss);
            Assert.AreEqual(EnemyKind.ForestGuardian, slain.KilledBy);
            Assert.AreEqual(RunFoeRank.Boss, slain.KilledByRank);
            Assert.AreEqual(0, slain.Kills, "смерть героя — не убийство");

            var burned = new RunStats();
            burned.Record(new List<SimEvent> { SimEvent.Death(-1, Hero, At) }, sim, bossId: boss);
            Assert.AreEqual(EnemyKind.None, burned.KilledBy);
            Assert.AreEqual(RunFoeRank.None, burned.KilledByRank);
        }

        [Test]
        public void FinishedStatsIgnoreFurtherRecords()
        {
            Simulation sim = Arena();
            int foe = Foe(sim, EnemyKind.ForestGuardian);
            var events = new List<SimEvent>
            {
                SimEvent.Damage(Hero, foe, 50, false, At, DamageType.Physical),
                SimEvent.Death(Hero, foe, At),
            };

            var stats = new RunStats(3, 40);
            stats.Record(events, sim, -1);
            stats.CountStep(true);
            stats.Finish(4, 10);

            stats.Record(events, sim, -1);
            stats.CountStep(true);
            stats.CountPotion(PotionKind.SmallHealth);
            stats.CountExperience(500, 3);
            stats.Finish(9, 0);

            Assert.IsTrue(stats.IsFinished);
            Assert.AreEqual(1, stats.Kills);
            Assert.AreEqual(50, stats.DamageDealt);
            Assert.AreEqual(1, stats.TotalTicks);
            Assert.AreEqual(0, stats.PotionsUsed);
            Assert.AreEqual(0, stats.LevelsGained);
            Assert.AreEqual(3, stats.StartLevel);
            Assert.AreEqual(40, stats.StartExperience);
            Assert.AreEqual(4, stats.EndLevel);
            Assert.AreEqual(10, stats.EndExperience);

            Assert.IsTrue(RunStats.Empty.IsFinished, "пустые итоги не пишутся");
            Assert.AreEqual(0, RunStats.Empty.Kills);
        }

        [Test]
        public void SummaryHashIgnoresStats()
        {
            Simulation sim = Arena();
            int foe = Foe(sim, EnemyKind.ForestGuardian);
            var stats = new RunStats();
            stats.Record(new List<SimEvent> { SimEvent.Death(Hero, foe, At) }, sim, -1);

            ulong bare = 17, counted = 17;
            new RunSummary(RunOutcome.Left, 2, 1, 1, 0, 30).HashInto(ref bare);
            new RunSummary(RunOutcome.Left, 2, 1, 1, 0, 30, stats: stats).HashInto(ref counted);
            Assert.AreEqual(bare, counted, "статистика не часть состояния игры");
            Assert.AreSame(RunStats.Empty, new RunSummary(RunOutcome.Left, 2, 1, 1, 0).Stats);
        }

        // ---- короткий забег через сессию ----

        private static GameSession Session(out Camp camp)
        {
            // Третий акт: портал в Разлом и алхимик с зельями.
            camp = new Camp(PrototypeContent.Items(), act: 3);
            camp.Earn(CurrencyType.Gold, 1000);
            Assert.IsTrue(camp.BuyPotion(PotionKind.SmallHealth));
            Assert.IsTrue(camp.BuyPotion(PotionKind.SmallHealth));
            return new GameSession(Seed, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds());
        }

        private static InputFrame Command(CampCommand command)
            => new InputFrame { Command = (byte)command, AttackTarget = -1, AbilityTarget = -1 };

        private static InputFrame RunInput(RunCommand command)
        {
            InputFrame input = InputFrame.Empty;
            input.Command = (byte)command;
            return input;
        }

        private static InputFrame Potion(PotionKind kind)
        {
            InputFrame input = InputFrame.Empty;
            input.PotionMask = Camp.PotionInputBit(kind);
            return input;
        }

        private static InputFrame Whirl()
        {
            InputFrame input = InputFrame.Empty;
            input.AbilityMask = 1;
            input.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            return input;
        }

        /// <summary>
        /// Поджигает всех живых врагов от имени героя насмерть: убийства идут
        /// штатным путём шага боя (TickBurning → Kill), а не правкой Alive.
        /// Возвращает, сколько врагов каждого вида стоит под огнём, и их опыт.
        /// </summary>
        private static int[] SetAllFoesOnFire(Simulation sim, out int total, out int xp)
        {
            var byKind = new int[256];
            total = 0;
            xp = 0;
            EntityStore e = sim.Entities;
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                sim.Statuses.ApplyBurn(i, Fix64.FromInt(100000), 3, Hero, -1);
                byKind[(int)e.Kind[i]]++;
                total++;
                xp += e.XpReward[i];
            }
            return byKind;
        }

        private static void SalvageIfReplacing(GameSession session, ref int steps)
        {
            if (session.Mode != GameMode.Rift || session.Run.Phase != RunPhase.ReplacingAbility) return;
            session.Step(RunInput(RunCommand.SalvageAbility));
            steps++;
        }

        /// <summary>
        /// Приёмка: зелье, зачистка поджогом, каст у выхода, полминуты на экране
        /// награды, награда, уход. Числа итогов сходятся с тем, что случилось,
        /// а время на экране награды идёт в общее, но не в боевое.
        /// </summary>
        [Test]
        public void ScriptedShortRun_ProducesTheExpectedTallies()
        {
            GameSession session = Session(out Camp camp);
            camp.GainExperience(95);
            Assert.AreSame(RunStats.Empty, session.LastRun.Stats, "до первого забега итоги пусты");
            Assert.AreSame(RunStats.Empty, session.CurrentRunStats);

            session.EnterRift();
            Assert.AreEqual(GameMode.Rift, session.Mode);
            Simulation sim = session.Run.Sim;
            RunStats stats = session.CurrentRunStats;
            Assert.AreNotSame(RunStats.Empty, stats);
            Assert.AreEqual(1, stats.StartLevel);
            Assert.AreEqual(95, stats.StartExperience);
            int steps = 0;

            // 1. Зелье в бою.
            sim.Entities.Health[0]--;
            session.Step(Potion(PotionKind.SmallHealth));
            steps++;
            Assert.AreEqual(1, stats.PotionsUsed);
            Assert.AreEqual(1, stats.PotionsUsedOf(PotionKind.SmallHealth));

            // 2. Зачистка: все враги горят от героя и гибнут в одном тике боя.
            int[] expectedByKind = SetAllFoesOnFire(sim, out int foes, out int xp);
            Assert.Greater(foes, 0, "в Разломе должны быть враги");
            session.Step(InputFrame.Empty);
            steps++;
            long burned = 0;
            foreach (SimEvent e in sim.Events)
                if (e.Type == SimEventType.DamageOverTime && e.Source == Hero) burned += e.Amount;
            Assert.AreEqual(RunPhase.SeekingExit, session.Run.Phase, "арена зачищена");
            Assert.AreEqual(foes, stats.Kills);
            for (int kind = 0; kind < expectedByKind.Length; kind++)
                Assert.AreEqual(expectedByKind[kind], stats.KillsOf((EnemyKind)kind), ((EnemyKind)kind).ToString());
            Assert.AreEqual(burned, stats.DamageOverTimeDealt);
            Assert.AreEqual(0, stats.Hits, "горение — не попадания");
            Assert.AreEqual(xp, stats.ExperienceGained);
            Assert.AreEqual(camp.Level - 1, stats.LevelsGained);
            Assert.GreaterOrEqual(stats.LevelsGained, 1, "95 + опыт убийств — новый уровень");

            // 3. Каст у выхода: последний тик боя перед экраном награды.
            sim.Entities.Position[Hero] = session.Run.Map.ExitPoint(0);
            session.Step(Whirl());
            steps++;
            Assert.AreEqual(RunPhase.ChoosingReward, session.Run.Phase);
            Assert.AreEqual(1, stats.AbilitiesCast);
            Assert.Greater(sim.Events.Count, 0, "на экране награды в симуляции лежат события прошлого тика");

            // 4. Полминуты на экране награды: бой стоит, прошлый тик дважды не считается.
            int kills = stats.Kills, casts = stats.AbilitiesCast;
            long dealt = stats.DamageDealt, taken = stats.DamageTaken;
            for (int i = 0; i < 30; i++)
            {
                session.Step(InputFrame.Empty);
                steps++;
            }
            Assert.AreEqual(3, stats.CombatTicks, "экран награды — не бой");
            Assert.AreEqual(steps, stats.TotalTicks, "экран награды — время забега");
            Assert.AreEqual(kills, stats.Kills);
            Assert.AreEqual(casts, stats.AbilitiesCast);
            Assert.AreEqual(dealt, stats.DamageDealt);
            Assert.AreEqual(taken, stats.DamageTaken);

            // 5. Награда и уход. Ручной уровень разработчика в повышения не идёт.
            session.Step(RunInput(RunCommand.ChooseReward1));
            steps++;
            SalvageIfReplacing(session, ref steps);
            int levels = stats.LevelsGained;
            camp.DeveloperGrantLevel();
            session.Step(RunInput(RunCommand.Leave));
            steps++;

            Assert.AreEqual(GameMode.Summary, session.Mode);
            Assert.AreSame(stats, session.LastRun.Stats, "итоги несут статистику этого забега");
            Assert.IsTrue(stats.IsFinished);
            Assert.AreEqual(steps, stats.TotalTicks);
            Assert.AreEqual(stats.TotalTicks / Simulation.TicksPerSecond, stats.TotalSeconds);
            Assert.AreEqual(levels, stats.LevelsGained);
            Assert.AreEqual(camp.Level, stats.EndLevel);
            Assert.AreEqual(camp.Experience, stats.EndExperience);
            Assert.AreEqual(EnemyKind.None, stats.KilledBy);

            // Экран итогов стоит — числа тоже.
            session.Step(InputFrame.Empty);
            Assert.AreEqual(steps, session.LastRun.Stats.TotalTicks);
        }

        /// <summary>
        /// Прямой удар через сессию: Вихрь по неподвижному врагу. Лучший удар —
        /// самое большое попадание героя, со способностью и видом цели.
        /// </summary>
        [Test]
        public void ScriptedWhirlwindHit_IsTheBestHit()
        {
            GameSession session = Session(out _);
            session.EnterRift();
            Simulation sim = session.Run.Sim;
            EntityStore e = sim.Entities;

            int foe = -1;
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                if (foe < 0) foe = i;
                else e.Alive[i] = false;
            }
            Assert.Greater(foe, 0);
            // Мишень вплотную, без хода и без удара: проверяется счёт, а не ИИ.
            e.Position[foe] = e.Position[Hero] + new FixVec2(Fix64.One, Fix64.Zero);
            e.Stats[foe].SetBase(StatType.MoveSpeed, Fix64.Zero);
            e.Stats[foe].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
            e.RefreshStats(foe);
            e.Health[foe] = e.MaxHealth[foe];
            e.NextAttackTick[foe] = int.MaxValue;

            int hits = 0, best = 0;
            long direct = 0;
            const int Ticks = 45;
            for (int t = 0; t < Ticks; t++)
            {
                session.Step(t == 0 ? Whirl() : InputFrame.Empty);
                foreach (SimEvent ev in sim.Events)
                {
                    if (ev.Type != SimEventType.Damage || ev.Source != Hero || ev.Target != foe) continue;
                    hits++;
                    direct += ev.Amount;
                    if (ev.Amount > best) best = ev.Amount;
                }
            }

            RunStats stats = session.CurrentRunStats;
            Assert.Greater(hits, 0, "Вихрь обязан задеть врага вплотную");
            Assert.AreEqual(hits, stats.Hits);
            Assert.AreEqual(direct, stats.DirectDamageDealt);
            Assert.AreEqual(best, stats.BestHit);
            Assert.AreEqual(e.Kind[foe], stats.BestHitTarget);
            Assert.AreEqual(DamageOrigin.Ability, stats.BestHitOrigin);
            Assert.AreEqual(AbilityDefinition.WhirlwindId, stats.BestHitAbility);
            Assert.AreEqual(1, stats.AbilitiesCast);
            Assert.AreEqual(Ticks, stats.CombatTicks);
            Assert.AreEqual(0, stats.Kills);
        }

        /// <summary>
        /// Каждый забег считает с нуля, а итоги прошлого заморожены: ни
        /// следующий забег, ни стенд врагов их уже не меняют.
        /// </summary>
        [Test]
        public void StatsResetPerRun()
        {
            GameSession session = Session(out Camp camp);
            session.EnterRift();
            SetAllFoesOnFire(session.Run.Sim, out int foes, out _);
            session.Run.Sim.Entities.Health[0]--;
            session.Step(Potion(PotionKind.SmallHealth));
            session.Step(RunInput(RunCommand.Leave));

            Assert.AreEqual(GameMode.Summary, session.Mode);
            RunStats first = session.LastRun.Stats;
            Assert.AreEqual(foes, first.Kills);
            Assert.AreEqual(1, first.PotionsUsed);
            Assert.AreEqual(2, first.TotalTicks);
            Assert.AreEqual(1, first.CombatTicks);
            long firstDamage = first.DamageDealt;

            session.Step(Command(CampCommand.RepeatRift));
            Assert.True(session.PreparationRequested);
            session.EnterRift();
            Assert.AreEqual(GameMode.Rift, session.Mode);
            RunStats second = session.CurrentRunStats;
            Assert.AreNotSame(first, second);
            Assert.AreEqual(0, second.Kills);
            Assert.AreEqual(0, second.KilledKindCount);
            Assert.AreEqual(0, second.TotalTicks);
            Assert.AreEqual(0, second.CombatTicks);
            Assert.AreEqual(0, second.PotionsUsed);
            Assert.AreEqual(0, second.DamageDealt);
            Assert.AreEqual(0, second.BestHit);
            Assert.AreEqual(0, second.LevelsGained);
            Assert.AreEqual(camp.Level, second.StartLevel);
            Assert.AreEqual(camp.Experience, second.StartExperience);

            SetAllFoesOnFire(session.Run.Sim, out _, out _);
            session.Run.Sim.Entities.Health[0]--;
            session.Step(Potion(PotionKind.SmallHealth));
            for (int i = 0; i < 9; i++) session.Step(InputFrame.Empty);
            Assert.AreEqual(10, second.TotalTicks);
            Assert.AreEqual(1, second.PotionsUsed);
            Assert.Greater(second.Kills, 0);

            Assert.AreEqual(foes, first.Kills, "итоги прошлого забега заморожены");
            Assert.AreEqual(1, first.PotionsUsed);
            Assert.AreEqual(2, first.TotalTicks);
            Assert.AreEqual(firstDamage, first.DamageDealt);

            // Стенд врагов из меню разработчика — тоже новый счёт.
            session.StartEnemySandbox(42, FixVec2.Zero, new EnemySandboxSpawn[0]);
            Assert.AreNotSame(second, session.CurrentRunStats);
            Assert.AreEqual(0, session.CurrentRunStats.TotalTicks);
        }
    }
}
