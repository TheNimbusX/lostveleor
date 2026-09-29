using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Правки ИИ по замерам «ощущения» (поток D плана «Мобы леса v2», выбор G2
    /// владельца «делай сам по цифрам», 29.09):
    ///
    ///   такт ударов (Simulation.AttackRhythm) — контакты разных мобов по герою
    ///   не ближе 8 тиков, после ближнего удара окно ответа 10 тиков (в лёгкой
    ///   пачке 24), по связанному и оглушённому не бьют — и по тому, кого вот-вот
    ///   оглушит таран, пока он на полосе; плод залпа в оглушённого не летит;
    ///   стрелок не стоит столбом (Simulation.EnemySurround.ShooterRestless) —
    ///   Плюй-плод между залпами переходит на соседнюю огневую точку;
    ///   последние идут сами (Simulation.EnemyBrain.LastStandAlert).
    /// </summary>
    public sealed class AttackRhythmTests
    {
        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        private static Simulation Arena()
        {
            var sim = new Simulation(777UL, 32);
            sim.SetupTestArena(0);
            sim.PlayerInvulnerable = true;
            return sim;
        }

        private static int Enemy(Simulation sim, FixVec2 at, EnemyKind kind = EnemyKind.ForestGuardian, int ready = 0,
            bool stationary = true)
        {
            int id = sim.SpawnEnemy(at, 1000, kind);
            if (stationary) sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.Facing[id] = (sim.Entities.Position[Simulation.PlayerId] - at).Normalized();
            sim.Entities.Aggro[id] = true;
            sim.Entities.NextAttackTick[id] = ready;
            return id;
        }

        private static EnemySwingState StepUntilSwing(Simulation sim, int id, int limit = 300)
        {
            for (int t = 0; t < limit; t++)
            {
                if (sim.TryGetEnemySwing(id, out var swing) && !swing.HitResolved) return swing;
                sim.Step(InputFrame.Empty);
            }
            Assert.Fail("моб " + id + " так и не замахнулся за " + limit + " тиков");
            return default;
        }

        // ---------- по одному ----------

        [Test]
        public void TwoReadyGuardians_SecondContactLandsASpacingLater()
        {
            var sim = Arena();
            int a = Enemy(sim, At(2, 0)), b = Enemy(sim, At(-2, 0));
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetEnemySwing(a, out var first), "первый готов — бьёт");
            Assert.IsFalse(sim.TryGetEnemySwing(b, out _), "второй удар лёг бы в тот же тик");

            var second = StepUntilSwing(sim, b);
            Assert.AreEqual(first.ImpactTick + Simulation.HeroContactSpacingTicks, second.ImpactTick,
                "второй ждёт ровно до разноса и не дольше");
        }

        [Test]
        public void MeleeCrowd_ContactsOnTheHeroNeverStack()
        {
            var sim = Arena();
            var crowd = new List<int>
            {
                Enemy(sim, At(2, 0)), Enemy(sim, At(-2, 0)), Enemy(sim, At(0, 2)),
                Enemy(sim, At(0.9, 0.9), EnemyKind.ForestRootSwarm), Enemy(sim, At(-0.9, -0.9), EnemyKind.ForestRootSwarm),
                Enemy(sim, At(0.9, -0.9), EnemyKind.ForestRootSwarm),
            };
            var seen = new HashSet<int>();
            var contacts = new List<KeyValuePair<int, int>>();
            for (int t = 0; t < 600; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (int id in crowd)
                    if (sim.TryGetEnemySwing(id, out var swing) && seen.Add(swing.Serial))
                        contacts.Add(new KeyValuePair<int, int>(swing.ImpactTick, id));
            }
            contacts.Sort((x, y) => x.Key.CompareTo(y.Key));
            Assert.Greater(contacts.Count, 20, "толпа почти не била — проверять нечего");
            for (int k = 1; k < contacts.Count; k++)
                if (contacts[k].Value != contacts[k - 1].Value)
                    Assert.GreaterOrEqual(contacts[k].Key - contacts[k - 1].Key, Simulation.HeroContactSpacingTicks,
                        "удары внахлёст на тике " + contacts[k].Key);
        }

        [Test]
        public void BudVolley_AndGuardianSwing_DoNotLandTogether()
        {
            var sim = Arena();
            var config = sim.ForestBudConfig;
            int bud = Enemy(sim, At(7, 0), EnemyKind.ForestBud);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetForestBudAttack(bud, out var volley), "залп не начался");
            int firstFruit = volley.FirstShotTick + config.FlightTicks;

            // Хранитель готов ровно так, что его удар лёг бы с первым плодом.
            int guardian = Enemy(sim, At(-2, 0), ready: firstFruit - Simulation.GuardianSwingWindupTicks);
            var swing = StepUntilSwing(sim, guardian);
            Assert.AreEqual(firstFruit + Simulation.HeroContactSpacingTicks, swing.ImpactTick,
                "замах ждёт, пока его удар отойдёт от плода на разнос");
        }

        // ---------- окно ответа ----------

        [Test]
        public void AfterAMeleeContact_NobodyStartsASwingForTheBreather()
        {
            var sim = Arena();
            int a = Enemy(sim, At(2, 0));
            // Второй готов в тот самый тик, когда первый бьёт: как бы ни шла
            // очередь мобов в этом тике, замахнуться он может только после окна.
            int b = Enemy(sim, At(-2, 0), ready: Simulation.GuardianSwingWindupTicks);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetEnemySwing(a, out var first));
            var second = StepUntilSwing(sim, b);
            Assert.AreEqual(first.ImpactTick + Simulation.HeroBreatherTicks, second.StartTick, "окно ответа 10 тиков");
            Assert.AreEqual(10, Simulation.HeroBreatherTicks);
        }

        [Test]
        public void EasyPack_HasTheLongerBreather()
        {
            var location = ArenaEncounterTests.ForestLocation();
            ArenaEncounterTemplate easy = null, other = null;
            foreach (var t in ForestEncounterTemplates.All)
            {
                if (t.Tier == EncounterTier.Easy && easy == null) easy = t;
                if (t.Tier != EncounterTier.Easy && t.Type == ArenaEncounterType.Normal && other == null) other = t;
            }
            Assert.IsNotNull(easy, "нет лёгкого шаблона");
            Assert.IsNotNull(other, "нет обычного не лёгкого шаблона");

            var sim = new Simulation(5UL, 128);
            Assert.AreEqual(Simulation.HeroBreatherTicks, sim.HeroBreatherTicksNow, "без встречи — обычное окно");
            sim.SetupArenaEncounter(ArenaEncounterTests.ArenaMap(location, easy.MinArena, 5UL), 5UL, easy.MinArena, 100, 100, easy);
            Assert.AreEqual(Simulation.EasyHeroBreatherTicks, sim.HeroBreatherTicksNow, easy.Key);
            sim.SetupArenaEncounter(ArenaEncounterTests.ArenaMap(location, other.MinArena, 5UL), 5UL, other.MinArena, 100, 100, other);
            Assert.AreEqual(Simulation.HeroBreatherTicks, sim.HeroBreatherTicksNow, other.Key);
            Assert.Greater(Simulation.EasyHeroBreatherTicks, Simulation.HeroBreatherTicks);
        }

        // ---------- связанного не бьют ----------

        [Test]
        public void RootedHero_TakesNoSwingUntilTheRootIsOver()
        {
            var sim = Arena();
            Assert.IsTrue(sim.ApplyHeroRoot(Simulation.RootSnarerRootTicks));
            int rootEnd = sim.Tick + sim.HeroRootTicksLeft;
            int a = Enemy(sim, At(2, 0));
            var swing = StepUntilSwing(sim, a);
            Assert.AreEqual(rootEnd + Simulation.HeroControlGraceTicks, swing.ImpactTick,
                "удар — сразу после освобождения и запаса, не раньше и не позже");
            Assert.AreEqual(swing.ImpactTick - Simulation.GuardianSwingWindupTicks, swing.StartTick);
        }

        [Test]
        public void StunnedHero_TakesNoBiteUntilTheStunIsOver()
        {
            var sim = Arena();
            Assert.IsTrue(sim.ApplyHeroStun(Simulation.StonehoofChargeStunTicks));
            int stunEnd = sim.Tick + sim.HeroStunTicksLeft;
            int swarm = Enemy(sim, At(1.1, 0), EnemyKind.ForestRootSwarm);
            var bite = StepUntilSwing(sim, swarm);
            Assert.GreaterOrEqual(bite.ImpactTick, stunEnd + Simulation.HeroControlGraceTicks);
        }

        [Test]
        public void StunnedHero_TakesNoTuskUntilTheStunIsOver()
        {
            // Ревью 29.09: таран оглушил героя, кабан встал рядом и бил клыками в
            // последний тик оглушения — отброс продлевал беспомощность. Клыки
            // спрашивают такт, как все атаки по герою.
            Assume.That(Simulation.StonehoofTuskEnabled, Is.True);
            for (int stunned = 0; stunned <= 1; stunned++)
            {
                var sim = Arena();
                int boar = sim.SpawnEnemy(At(0, -1.4), 1000, EnemyKind.ForestStonehoof);
                sim.Entities.Facing[boar] = new FixVec2(Fix64.Zero, Fix64.One);
                sim.Entities.Aggro[boar] = true;
                sim.Entities.NextAttackTick[boar] = 10000;
                int free = 0;
                if (stunned == 1)
                {
                    Assert.IsTrue(sim.ApplyHeroStun(Simulation.StonehoofChargeStunTicks));
                    free = sim.Tick + sim.HeroStunTicksLeft;
                }
                sim.Step(InputFrame.Empty);
                if (stunned == 0)
                {
                    Assert.IsTrue(sim.TryGetStonehoofTusk(boar, out _), "без оглушения клыки — в первый же тик");
                    continue;
                }
                for (int t = 0; t < 90; t++)
                {
                    if (sim.TryGetStonehoofTusk(boar, out var tusk))
                        Assert.GreaterOrEqual(tusk.ImpactTick, free + Simulation.HeroControlGraceTicks,
                            "клыки по оглушённому, тик " + sim.Tick);
                    sim.Step(InputFrame.Empty);
                }
            }
        }

        /// <summary>Кабан у (5, 0) смотрит на героя в нуле: первый же шаг — разгон тарана по герою.</summary>
        private static Simulation ChargeArena()
        {
            var sim = new Simulation(76, 64);
            sim.SetupStonehoofEncounter(null, 76, 1);
            sim.Entities.Position[0] = FixVec2.Zero;
            sim.Entities.Position[1] = At(5, 0);
            sim.Entities.Facing[1] = new FixVec2(-Fix64.One, Fix64.Zero);
            sim.Entities.NextAttackTick[1] = 0;
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.RefreshStats(0);
            sim.Entities.Health[0] = 10000;
            return sim;
        }

        [Test]
        public void HeroInTheChargeLane_NoSwingLandsInTheComingStun()
        {
            var sim = ChargeArena();
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetStonehoofAction(1, out var charge), "таран не начался");
            // Хранитель готов так, что его удар лёг бы через пару тиков после тарана.
            // Стоит с той стороны, куда таран отбросит героя (вбок, на метр).
            int guardian = Enemy(sim, At(0, -2), ready: charge.LaunchTick - Simulation.GuardianSwingWindupTicks + 14);
            int stunAt = -1, impact = -1;
            for (int t = 0; t < 200 && (stunAt < 0 || impact < 0); t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.HeroControl && ev.Source == 1) stunAt = sim.Tick;
                if (impact < 0 && sim.TryGetEnemySwing(guardian, out var swing)) impact = swing.ImpactTick;
            }
            Assert.Greater(stunAt, 0, "таран не оглушил героя — проверять нечего");
            Assert.Greater(impact, 0, "Хранитель так и не замахнулся");
            Assert.GreaterOrEqual(impact, stunAt + Simulation.StonehoofChargeStunTicks,
                "удар Хранителя лёг бы на оглушённого тараном героя");
        }

        [Test]
        public void HeroOffTheChargeLane_SwingsAsUsual()
        {
            var sim = ChargeArena();
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetStonehoofAction(1, out var charge), "таран не начался");
            // Герой ушёл с полосы (разгон уже зафиксирован): окна оглушения нет.
            sim.Entities.Position[0] = At(0, 4);
            int ready = charge.LaunchTick - Simulation.GuardianSwingWindupTicks + 14;
            int guardian = Enemy(sim, At(2, 4), ready: ready);
            var swing = StepUntilSwing(sim, guardian);
            Assert.AreEqual(ready, swing.StartTick, "с полосы ушёл — Хранитель бьёт, как только готов");
        }

        [Test]
        public void BudVolley_NoFruitFliesAtAStunnedHero()
        {
            var sim = Arena();
            var config = sim.ForestBudConfig;
            int bud = Enemy(sim, At(7, 0), EnemyKind.ForestBud);
            int launched = 0;
            for (int t = 0; t < 120 && launched == 0; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.ForestFruitLaunched && ev.Source == bud) launched++;
            }
            Assert.AreEqual(1, launched, "первый плод залпа не вылетел");
            Assert.Greater(config.ShotCount, 1, "в залпе один плод — прерывать нечего");

            // Плод летит полторы секунды — оглушение длиннее полёта, чтобы следующий
            // плод залпа упал бы ещё на оглушённого (в бою так бывает с тараном,
            // когда герой на полосе: HeroInTheChargeLane_…).
            Assert.IsTrue(sim.ApplyHeroStun(config.FlightTicks + 30));
            int free = sim.Tick + sim.HeroStunTicksLeft + Simulation.HeroControlGraceTicks;
            bool cancelled = false;
            for (int t = 0; t < config.ShotCount * config.ShotIntervalTicks + 10; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                {
                    if (ev.Type == SimEventType.ForestBudVolleyCancelled && ev.Source == bud) cancelled = true;
                    if (ev.Type == SimEventType.ForestFruitLaunched && ev.Source == bud
                        && sim.TryGetForestFruit(ev.Amount, out var fruit))
                        Assert.GreaterOrEqual(fruit.ImpactTick, free, "плод летит в оглушённого героя");
                }
            }
            Assert.IsTrue(cancelled, "залп по оглушённому не прервался");
        }

        [Test]
        public void RhythmSwitch_IsHashed_AndOnByDefault()
        {
            var a = Arena();
            var b = Arena();
            Assert.IsTrue(a.AttackRhythmEnabled);
            Assert.AreEqual(a.StateHash(), b.StateHash());
            b.AttackRhythmEnabled = false;
            Assert.AreNotEqual(a.StateHash(), b.StateHash(), "выключенный такт — другой бой, и хеш обязан это видеть");
        }

        // ---------- стрелок не стоит столбом ----------

        [Test]
        public void Bud_BetweenVolleys_WalksToTheNextFiringPoint()
        {
            var sim = Arena();
            int bud = Enemy(sim, At(7, 0), EnemyKind.ForestBud, ready: 100000, stationary: false);
            FixVec2 start = sim.Entities.Position[bud];
            Assert.IsTrue(sim.ShooterRestless(bud), "между залпами стрелок — непоседа");
            Fix64 moved = Fix64.Zero;
            for (int t = 0; t < 120; t++)
            {
                FixVec2 before = sim.Entities.Position[bud];
                sim.Step(InputFrame.Empty);
                moved += FixVec2.Distance(before, sim.Entities.Position[bud]);
            }
            double distance = sim.Entities.Position[bud].Length.ToDouble();
            Assert.Greater(moved.ToDouble(), 2.0, "стоит столбом");
            Assert.Greater(FixVec2.Distance(start, sim.Entities.Position[bud]).ToDouble(), 1.0, "топчется на месте");
            Assert.That(distance, Is.InRange(5.5, 9.0), "уходит по кругу, а не к герою или прочь");
        }

        [Test]
        public void Bud_DropsAStaleFiringPoint_AfterTheWalkLimit()
        {
            // Ревью 29.09: точка непоседы — место в мире, взятое от героя в тик
            // выбора. Не дошёл (здесь — стоит на месте), а герой ушёл — через
            // RestlessWalkMaxTicks точка выбирается заново от нового места героя.
            var sim = Arena();
            int bud = Enemy(sim, At(7, 0), EnemyKind.ForestBud, ready: 100000, stationary: false);
            sim.Step(InputFrame.Empty);
            int picked = sim.Tick - 1;
            Assert.IsTrue(sim.TryGetRangedGoal(bud, out FixVec2 first), "непоседа не выбрал точку");
            sim.Entities.Stats[bud].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(bud);
            sim.Entities.Position[Simulation.PlayerId] = At(0, 3);

            // На полпути не передумывает: срок выбора (до SurroundAssignTicks) плюс предел.
            while (sim.Tick <= picked + Simulation.RestlessWalkMaxTicks)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsTrue(sim.TryGetRangedGoal(bud, out FixVec2 goal) && goal.Equals(first),
                    "передумал раньше предела, тик " + sim.Tick);
            }
            while (sim.Tick <= picked + Simulation.RestlessWalkMaxTicks + Simulation.SurroundAssignTicks)
                sim.Step(InputFrame.Empty);
            sim.TryGetRangedGoal(bud, out FixVec2 fresh);
            Assert.IsFalse(fresh.Equals(first), "идёт к устаревшей точке дольше предела");
            // Новая точка — от нового места героя, на прежней дальности.
            double radius = FixVec2.Distance(fresh, sim.Entities.Position[Simulation.PlayerId]).ToDouble();
            double own = FixVec2.Distance(sim.Entities.Position[bud], sim.Entities.Position[Simulation.PlayerId]).ToDouble();
            Assert.That(radius, Is.EqualTo(own).Within(0.05), "новая точка не на круге вокруг героя");
        }

        [Test]
        public void Bud_InTheVolley_IsNotRestless()
        {
            var sim = Arena();
            int bud = Enemy(sim, At(7, 0), EnemyKind.ForestBud);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetForestBudAttack(bud, out _));
            Assert.IsFalse(sim.ShooterRestless(bud), "в залпе стрелок стоит");
        }

        [Test]
        public void Snarer_IsRestlessOnlyWhenReadyAndStillWaiting()
        {
            var sim = Arena();
            int waiting = Enemy(sim, At(12, 0), EnemyKind.ForestRootSnarer, ready: 0);
            int cooling = Enemy(sim, At(-12, 0), EnemyKind.ForestRootSnarer, ready: 100000);
            for (int t = 0; t < Simulation.RestlessAfterTicks; t++)
            {
                Assert.IsFalse(sim.ShooterRestless(waiting), "ждёт меньше RestlessAfterTicks, тик " + sim.Tick);
                sim.Step(InputFrame.Empty);
            }
            Assert.IsTrue(sim.ShooterRestless(waiting), "готов и ждёт — меняет точку");
            Assert.IsFalse(sim.ShooterRestless(cooling), "на перезарядке Корнехват точку не меняет");
        }

        // ---------- последние идут сами ----------

        [Test]
        public void LastTwoEnemies_NoticeTheHeroWithoutSeeingHim()
        {
            var location = ArenaEncounterTests.ForestLocation();
            ArenaEncounterTemplate template = null;
            foreach (var t in ForestEncounterTemplates.All)
                if (t.Type == ArenaEncounterType.Normal && template == null) template = t;
            var map = ArenaEncounterTests.ArenaMap(location, template.MinArena, 11UL);
            var sim = new Simulation(11UL, 128);
            sim.SetupArenaEncounter(map, 11UL, template.MinArena, 100, 100, template);
            sim.PlayerInvulnerable = true;
            var e = sim.Entities;
            var wave = new List<int>();
            for (int i = 1; i < e.Count; i++) if (e.Alive[i]) wave.Add(i);
            Assert.Greater(wave.Count, Simulation.LastStandEnemies, "стартовая волна меньше порога");

            for (int t = 0; t < 10; t++) sim.Step(InputFrame.Empty);
            foreach (int id in wave) Assert.IsFalse(e.Aggro[id], "бой не начался, а " + id + " уже заметил героя");

            // Остаются двое — оба дальше радиуса обнаружения.
            for (int k = Simulation.LastStandEnemies; k < wave.Count; k++)
                sim.ApplyAbilityDamage(Simulation.PlayerId, wave[k], 1000000, -1, DamageType.Physical);
            int last1 = wave[0], last2 = wave[1];
            Assert.Greater(FixVec2.Distance(e.Position[0], e.Position[last1]).ToDouble(), 7.0);
            Assert.Greater(FixVec2.Distance(e.Position[0], e.Position[last2]).ToDouble(), 7.0);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(e.Aggro[last1] && e.Aggro[last2], "последние двое так и стоят вдали");
        }
    }
}
