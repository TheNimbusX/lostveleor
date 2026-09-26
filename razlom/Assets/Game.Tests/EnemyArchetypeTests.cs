using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Таблица видов и баланс v1 (стадия 2 плана «Мобы леса»).
    ///
    /// Эталон — герой 5-го уровня лагеря: 150 базы локации + 4 × 30 = 270
    /// здоровья, 34 + 4 × 5 = 54 урона, удар раз в 20 тиков. Время убийства
    /// считается ЦЕЛЫМИ базовыми ударами без критов и способностей: это
    /// верхняя оценка, настоящий бой быстрее. Живой ассет Meadow проверяет
    /// MeadowBalanceTests в редакторе; здесь — все виды таблицы, в том числе
    /// Камнекопыт, Вендиго и новые мобы леса, которых в пачках Meadow пока нет.
    /// </summary>
    public sealed class EnemyArchetypeTests
    {
        private const int ReferenceCampLevel = 5;
        private const int LocationPlayerHealth = 150;
        private const int ReferenceHealth = LocationPlayerHealth + 4 * Progression.HealthPerLevel;

        // Стенд баланса (26.09): укус роя 5 → 4 → 3, Хранитель 450 → 550 здоровья,
        // коготь Вендиго 32 → 26 (проход 2).
        [TestCase(EnemyKind.ForestRootSwarm, 130, 3, 1)]
        [TestCase(EnemyKind.ForestGuardian, 550, 14, 2)]
        [TestCase(EnemyKind.ForestBud, 300, 11, 2)]
        [TestCase(EnemyKind.ForestStonehoof, 650, 24, 3)]
        [TestCase(EnemyKind.ForestWendigo, 2000, 26, 6)]
        // Новые мобы леса (план от 26.09). Угроза Расщепеня — вместе с детьми.
        [TestCase(EnemyKind.ForestThorncaster, 1700, 30, 6)]
        [TestCase(EnemyKind.ForestRootSnarer, 520, 20, 3)]
        [TestCase(EnemyKind.ForestSplitter, 420, 12, 4)]
        [TestCase(EnemyKind.ForestSplitling, 120, 4, 1)]
        public void Table_HoldsBalanceV1(EnemyKind kind, int health, int damage, int threat)
        {
            var a = EnemyArchetypes.Get(kind);
            Assert.That(a.Kind, Is.EqualTo(kind));
            Assert.That(a.BaseHealth, Is.EqualTo(health));
            Assert.That(a.BaseDamage, Is.EqualTo(damage));
            Assert.That(a.Threat, Is.EqualTo(threat));
            Assert.That(a.BodyRadius, Is.LessThanOrEqualTo(EntityStore.MaxBodyRadius));
            Assert.That(a.CycleTicks, Is.GreaterThanOrEqualTo(a.WindupTicks + a.RecoveryTicks));
        }

        [Test]
        public void Table_MirrorsSimulationWindowsAndBodies_InEnemyKindOrder()
        {
            Assert.That(EnemyArchetypes.Count, Is.EqualTo(9));
            for (int i = 0; i < EnemyArchetypes.Count; i++)
                Assert.That((int)EnemyArchetypes.At(i).Kind, Is.EqualTo(i + 1));
            // Значения видов сериализованы: новые только в конец, без перенумерации.
            Assert.That((int)EnemyKind.ForestThorncaster, Is.EqualTo(6));
            Assert.That((int)EnemyKind.ForestRootSnarer, Is.EqualTo(7));
            Assert.That((int)EnemyKind.ForestSplitter, Is.EqualTo(8));
            Assert.That((int)EnemyKind.ForestSplitling, Is.EqualTo(9));

            var guardian = EnemyArchetypes.Get(EnemyKind.ForestGuardian);
            Assert.That(guardian.WindupTicks, Is.EqualTo(Simulation.EnemyAttackWindupTicks));
            Assert.That(guardian.RecoveryTicks, Is.EqualTo(Simulation.GuardianSwingRecoveryTicks));
            Assert.That(guardian.CycleTicks, Is.EqualTo(Simulation.GuardianSwingCycleTicks));
            var swarm = EnemyArchetypes.Get(EnemyKind.ForestRootSwarm);
            Assert.That(swarm.WindupTicks, Is.EqualTo(Simulation.RootSwarmAttackWindupTicks));
            Assert.That(swarm.CycleTicks, Is.EqualTo(Simulation.RootSwarmAttackCooldownTicks));
            var bud = EnemyArchetypes.Get(EnemyKind.ForestBud);
            Assert.That(bud.WindupTicks, Is.EqualTo(ForestBudSettings.Default.WindupTicks));
            Assert.That(bud.CycleTicks, Is.EqualTo(ForestBudSettings.Default.AttackCooldownTicks));
            Assert.That(bud.BaseHealth, Is.EqualTo(ForestBudSettings.Default.Health));
            Assert.That(bud.BaseDamage, Is.EqualTo(ForestBudSettings.Default.Damage));
            Assert.That(bud.BodyRadius, Is.EqualTo(ForestBudSettings.Default.BodyRadius));
            Assert.That(EnemyArchetypes.Get(EnemyKind.ForestStonehoof).BodyRadius, Is.EqualTo(Simulation.StonehoofRadius));
            Assert.That(EnemyArchetypes.Get(EnemyKind.ForestWendigo).BodyRadius, Is.EqualTo(Simulation.WendigoBodyRadius));
            Assert.That(EnemyArchetypes.Get(EnemyKind.ForestWendigo).CycleTicks, Is.EqualTo(45));

            // Шипомёт: первый шип на 27-м тике, последний на 45-м, 30 стоит, линия раз в 150.
            var thorn = EnemyArchetypes.Get(EnemyKind.ForestThorncaster);
            Assert.That(thorn.WindupTicks, Is.EqualTo(27));
            Assert.That(Simulation.ThornLineLastImpactTicks, Is.EqualTo(45));
            Assert.That(thorn.WindupTicks + thorn.RecoveryTicks, Is.EqualTo(45 + 30));
            Assert.That(thorn.CycleTicks, Is.EqualTo(150));
            Assert.That(thorn.BodyRadius, Is.EqualTo(Fix64.Ratio(80, 100)));
            // Корнехват: 15 тиков позы, контакт через 21 после удара корнями, 36 стоит.
            var snarer = EnemyArchetypes.Get(EnemyKind.ForestRootSnarer);
            Assert.That(snarer.WindupTicks, Is.EqualTo(36));
            Assert.That(snarer.RecoveryTicks, Is.EqualTo(36));
            Assert.That(snarer.CycleTicks, Is.EqualTo(150));
            Assert.That(snarer.BodyRadius, Is.EqualTo(Fix64.Ratio(75, 100)));
            Assert.That(EnemyArchetypes.Get(EnemyKind.ForestSplitter).BodyRadius, Is.EqualTo(Fix64.Ratio(70, 100)));
            Assert.That(EnemyArchetypes.Get(EnemyKind.ForestSplitling).BodyRadius, Is.EqualTo(Fix64.Ratio(42, 100)));

            // Кто бьёт общим замахом, у того таблица повторяет профиль замаха.
            foreach (var kind in new[] { EnemyKind.ForestGuardian, EnemyKind.ForestRootSwarm,
                EnemyKind.ForestSplitter, EnemyKind.ForestSplitling })
            {
                var row = EnemyArchetypes.Get(kind);
                var profile = Simulation.MeleeProfileOf(kind);
                Assert.That(profile.WindupTicks, Is.EqualTo(row.WindupTicks), kind.ToString());
                Assert.That(profile.RecoveryTicks, Is.EqualTo(row.RecoveryTicks), kind.ToString());
            }
            Assert.That(Simulation.MeleeProfileOf(EnemyKind.ForestSplitter).WindupTicks, Is.EqualTo(18));
            Assert.That(Simulation.MeleeProfileOf(EnemyKind.ForestSplitter).RecoveryTicks, Is.EqualTo(12));

            // Моб без вида — только тесты и стенды — живёт по правилам Хранителя.
            Assert.That(EnemyArchetypes.Get(EnemyKind.None).Kind, Is.EqualTo(EnemyKind.ForestGuardian));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => EnemyArchetypes.Get((EnemyKind)99));
        }

        [Test]
        public void DepthScaling_SevenPercentHealthAndEightPercentDamagePerArena()
        {
            Assert.That(EnemyArchetypes.DepthHealthPercent(1), Is.EqualTo(100));
            Assert.That(EnemyArchetypes.DepthHealthPercent(2), Is.EqualTo(107));
            Assert.That(EnemyArchetypes.DepthHealthPercent(8), Is.EqualTo(149));
            Assert.That(EnemyArchetypes.DepthHealthPercent(10), Is.EqualTo(163));
            Assert.That(EnemyArchetypes.DepthDamagePercent(1), Is.EqualTo(100));
            Assert.That(EnemyArchetypes.DepthDamagePercent(8), Is.EqualTo(156));
            Assert.That(EnemyArchetypes.DepthHealthPercent(0), Is.EqualTo(100));

            // Округление до ближайшего; long внутри — большие проценты не переполняют int.
            Assert.That(EnemyArchetypes.ScaleHealth(450, 149), Is.EqualTo(671));
            Assert.That(EnemyArchetypes.ScaleHealth(130, 107), Is.EqualTo(139));
            Assert.That(EnemyArchetypes.ScaleHealth(450, 100, EnemyArchetypes.HardRoutePercent), Is.EqualTo(563));
            Assert.That(EnemyArchetypes.ScaleHealth(8000, 1000, 1000), Is.EqualTo(800000));
            Assert.That(EnemyArchetypes.ScaleHealth(1, 1, 1), Is.EqualTo(1));
            Assert.That(RiftLevelSettings.Prototype(3).EnemyHealth, Is.EqualTo(114));
        }

        [Test]
        public void Configure_WritesArchetypeIntoStatSheet_AndEnemiesNeverCrit()
        {
            var sim = new Simulation(3, 16);
            sim.SetupTestArena(0);
            for (int i = 0; i < EnemyArchetypes.Count; i++)
            {
                var a = EnemyArchetypes.At(i);
                int id = sim.SpawnEnemy(new FixVec2(Fix64.FromInt(3 * i + 3), Fix64.Zero), 77, a.Kind);
                Assert.That(sim.Entities.Kind[id], Is.EqualTo(a.Kind));
                Assert.That(sim.Entities.Damage[id], Is.EqualTo(a.BaseDamage), a.Kind.ToString());
                Assert.That(sim.Entities.BodyRadius[id], Is.EqualTo(a.BodyRadius), a.Kind.ToString());
                Assert.That(sim.Entities.CritChance[id], Is.EqualTo(Fix64.Zero), a.Kind.ToString());
                // Здоровье приходит из Spawn: его считает расстановка, а не Configure.
                Assert.That(sim.Entities.MaxHealth[id], Is.EqualTo(77));
                Assert.That(sim.ArchetypeHealth(a.Kind), Is.EqualTo(a.BaseHealth));
                // Опыт: Шипомёт — элита сам по себе, как Вендиго; детёныш — пять.
                int xp = a.Kind == EnemyKind.ForestWendigo || a.Kind == EnemyKind.ForestThorncaster
                    ? Progression.EliteKillXp
                    : a.Kind == EnemyKind.ForestSplitling ? Simulation.SplitlingKillXp : Progression.NormalKillXp;
                Assert.That(sim.Entities.XpReward[id], Is.EqualTo(xp), a.Kind.ToString());
            }
        }

        [Test]
        public void Splitling_IsSpawnOnly_AndSplitterReservesPlacesForItsChildren()
        {
            for (int i = 0; i < EnemyArchetypes.Count; i++)
            {
                var kind = EnemyArchetypes.At(i).Kind;
                Assert.That(EnemyArchetypes.IsPlaceable(kind), Is.EqualTo(kind != EnemyKind.ForestSplitling), kind.ToString());
                Assert.That(EnemyArchetypes.BodiesPerSpawn(kind), Is.EqualTo(kind == EnemyKind.ForestSplitter ? 3 : 1),
                    kind.ToString());
            }
            Assert.That(Simulation.SplitChildren, Is.EqualTo(2));
            Assert.That(EnemyArchetypes.IsPlaceable(EnemyKind.None), Is.False);
            Assert.That(EnemyArchetypes.IsPlaceable((EnemyKind)99), Is.False);

            // Детёныша не поставить ни в пачку, ни в волну.
            Assert.Throws<System.ArgumentException>(() => new EncounterGroup(EnemyKind.ForestSplitling, 1, 1));
            Assert.Throws<System.ArgumentException>(() => new WaveGroup(EnemyKind.ForestSplitling, 1, 1, WavePlacement.Front));

            // Ёмкость считает тела: Расщепень — три места.
            var pack = new EncounterPack(1, 100, new[] {
                new EncounterGroup(EnemyKind.ForestSplitter, 1, 2),
                new EncounterGroup(EnemyKind.ForestRootSwarm, 2, 3, growWithDepth: true) });
            Assert.That(pack.MaxEnemies(1), Is.EqualTo(2 * 3 + 4));
            var wave = new EncounterWave(WaveTrigger.Start,
                new WaveGroup(EnemyKind.ForestSplitter, 1, 1, WavePlacement.Front),
                new WaveGroup(EnemyKind.ForestGuardian, 1, 2, WavePlacement.Flank));
            Assert.That(wave.MaxEnemies, Is.EqualTo(3 + 2));
        }

        // Стенд одного вида — для тестов мобов и съёмки: строка таблицы, выросшая до арены.
        [TestCase(EnemyKind.ForestThorncaster, 1, 1, 100)]
        [TestCase(EnemyKind.ForestRootSnarer, 3, 8, 100)]
        [TestCase(EnemyKind.ForestSplitter, 3, 8, EnemyArchetypes.HardRoutePercent)]
        [TestCase(EnemyKind.ForestGuardian, 2, 5, 100)]
        public void KindTestArena_SpawnsTheArchetypeScaledToTheArena(EnemyKind kind, int count, int arena, int hard)
        {
            var sim = new Simulation(5, 32);
            var plan = sim.SetupKindTestArena(kind, count, arena: arena, hardPercent: hard);
            var row = EnemyArchetypes.Get(kind);
            Assert.That(sim.Entities.Count, Is.EqualTo(1 + count));
            for (int id = 1; id <= count; id++)
            {
                Assert.That(sim.Entities.Kind[id], Is.EqualTo(kind));
                Assert.That(sim.Entities.MaxHealth[id], Is.EqualTo(EnemyArchetypes.ScaleHealth(row.BaseHealth,
                    EnemyArchetypes.DepthHealthPercent(arena), hard)));
                Assert.That(sim.Entities.Health[id], Is.EqualTo(sim.Entities.MaxHealth[id]));
                double damage = row.BaseDamage * EnemyArchetypes.DepthDamagePercent(arena) * hard / 10000.0;
                Assert.That(sim.Entities.Damage[id], Is.EqualTo(damage).Within(0.51), kind + " урон");
                Assert.That(sim.Entities.BodyRadius[id], Is.EqualTo(row.BodyRadius));
                Assert.That(sim.Entities.Aggro[id], Is.True);
                Assert.That(plan.IsElite(id), Is.EqualTo(kind == EnemyKind.ForestThorncaster));
                double distance = FixVec2.Distance(sim.Entities.Position[id], sim.Entities.Position[0]).ToDouble();
                Assert.That(distance, Is.GreaterThanOrEqualTo(6.0 - 1e-3), "шеренга на 6 м от героя");
            }
            // Тела шеренги не налезают друг на друга.
            for (int i = 1; i <= count; i++)
                for (int j = i + 1; j <= count; j++)
                    Assert.That(FixVec2.Distance(sim.Entities.Position[i], sim.Entities.Position[j]).ToDouble(),
                        Is.GreaterThan((row.BodyRadius * 2).ToDouble()));

            var again = new Simulation(5, 32);
            again.SetupKindTestArena(kind, count, arena: arena, hardPercent: hard);
            Assert.That(again.StateHash(), Is.EqualTo(sim.StateHash()));
        }

        [Test]
        public void Shares_RoundToNearest_FromTheLiveNumbers()
        {
            Assert.That(EnemyArchetypes.Share(EnemyArchetypes.ThorncasterSpikeDamage,
                EnemyArchetypes.ThorncasterBurstDamage, EnemyArchetypes.ThorncasterSpikeDamage), Is.EqualTo(22));
            // Детёныш табличного родителя — строка детёныша.
            Assert.That(EnemyArchetypes.Share(EnemyArchetypes.SplitterHealth, EnemyArchetypes.SplitlingHealth,
                EnemyArchetypes.SplitterHealth), Is.EqualTo(EnemyArchetypes.SplitlingHealth));
            Assert.That(EnemyArchetypes.Share(EnemyArchetypes.SplitterDamage, EnemyArchetypes.SplitlingDamage,
                EnemyArchetypes.SplitterDamage), Is.EqualTo(EnemyArchetypes.SplitlingDamage));
            // Родитель восьмой арены: 626 здоровья → 179 (178,86 до ближайшего).
            Assert.That(EnemyArchetypes.Share(EnemyArchetypes.ScaleHealth(420, 149), 120, 420), Is.EqualTo(179));
            Assert.That(EnemyArchetypes.Share(0, 120, 420), Is.Zero);

            var sim = new Simulation(3, 16);
            sim.SetupTestArena(0);
            int thorn = sim.SpawnEnemy(new FixVec2(Fix64.FromInt(5), Fix64.Zero), 100, EnemyKind.ForestThorncaster);
            Assert.That(sim.ThornSpikeDamageOf(thorn), Is.EqualTo(30));
            Assert.That(sim.ThornBurstDamageOf(thorn), Is.EqualTo(22));
        }

        [Test]
        public void HitSizes_AgainstTheReferenceHero()
        {
            // Обычный удар — до 8% от 270, крупная фигура — 8–16%.
            foreach (var kind in new[] { EnemyKind.ForestRootSwarm, EnemyKind.ForestGuardian, EnemyKind.ForestBud,
                EnemyKind.ForestRootSnarer, EnemyKind.ForestSplitter, EnemyKind.ForestSplitling })
                Assert.That(EnemyArchetypes.Get(kind).BaseDamage * 100, Is.LessThanOrEqualTo(8 * ReferenceHealth), kind.ToString());
            Assert.That(EnemyArchetypes.Get(EnemyKind.ForestGuardian).BaseDamage * 100,
                Is.GreaterThanOrEqualTo(4 * ReferenceHealth), "удар Хранителя не должен быть щекоткой");
            int[] big =
            {
                EnemyArchetypes.Get(EnemyKind.ForestStonehoof).BaseDamage,
                EnemyArchetypes.WendigoClawDamage, EnemyArchetypes.WendigoLeapDamage, EnemyArchetypes.WendigoHowlDamage,
                EnemyArchetypes.ThorncasterSpikeDamage, EnemyArchetypes.ThorncasterBurstDamage,
            };
            foreach (int damage in big)
            {
                Assert.That(damage * 100, Is.GreaterThanOrEqualTo(8 * ReferenceHealth), damage.ToString());
                Assert.That(damage * 100, Is.LessThanOrEqualTo(16 * ReferenceHealth), damage.ToString());
            }
            Assert.That(EnemyArchetypes.WendigoShare(EnemyArchetypes.WendigoClawDamage, EnemyArchetypes.WendigoLeapDamage),
                Is.EqualTo(EnemyArchetypes.WendigoLeapDamage));
            Assert.That(EnemyArchetypes.WendigoShare(0, EnemyArchetypes.WendigoLeapDamage), Is.Zero);
        }

        private static EncounterSettings AllKinds(int arena)
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 2);
            var swarm = new EncounterGroup(EnemyKind.ForestRootSwarm, 2, 3);
            var bud = new EncounterGroup(EnemyKind.ForestBud, 1, 1);
            var stonehoof = new EncounterGroup(EnemyKind.ForestStonehoof, 1, 1);
            var wendigo = new EncounterGroup(EnemyKind.ForestWendigo, 1, 1, elite: true);
            var elite = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1, elite: true);
            var thorncaster = new EncounterGroup(EnemyKind.ForestThorncaster, 1, 1, elite: true);
            var snarer = new EncounterGroup(EnemyKind.ForestRootSnarer, 1, 1);
            var splitter = new EncounterGroup(EnemyKind.ForestSplitter, 1, 1);
            var main = new[]
            {
                new EncounterPack(2, 100, new[] { guardian, swarm }), new EncounterPack(3, 100, new[] { bud, guardian }),
                new EncounterPack(4, 100, new[] { stonehoof, swarm }), new EncounterPack(5, 100, new[] { wendigo }),
                new EncounterPack(8, 100, new[] { snarer, guardian }), new EncounterPack(9, 100, new[] { splitter, swarm }),
                new EncounterPack(10, 100, new[] { thorncaster }),
            };
            return new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) }, main,
                new[] { new EncounterPack(6, 100, new[] { stonehoof }) },
                new[] { new EncounterPack(7, 100, new[] { elite, wendigo }) },
                4, 0, EnemyArchetypes.DepthDamagePercent(arena), Fix64.FromInt(5));
        }

        /// <summary>Полосы времени убийства эталонным героем, секунды целых базовых ударов.</summary>
        private static void KillBand(EnemyKind kind, out double min, out double max)
        {
            switch (kind)
            {
                case EnemyKind.ForestRootSwarm: min = 1; max = 3; break;
                // 550 здоровья: 11 ударов на первой арене, 16 (10,7 с) на восьмой.
                case EnemyKind.ForestGuardian: min = 4; max = 12; break;
                case EnemyKind.ForestBud: min = 3; max = 7; break;
                case EnemyKind.ForestStonehoof: min = 6; max = 13; break;
                case EnemyKind.ForestWendigo: min = 20; max = 40; break;
                // 1700 здоровья: 32 удара на первой арене (21 с), 47 на восьмой.
                case EnemyKind.ForestThorncaster: min = 20; max = 40; break;
                case EnemyKind.ForestRootSnarer: min = 4; max = 12; break;
                case EnemyKind.ForestSplitter: min = 3; max = 9; break;
                default: min = 0; max = 0; Assert.Fail("Нет полосы для " + kind); break;
            }
        }

        private static double KillSeconds(Simulation sim, int enemy)
        {
            int damage = sim.Entities.Damage[Simulation.PlayerId];
            int hits = (sim.Entities.MaxHealth[enemy] + damage - 1) / damage;
            return hits * Simulation.PlayerBaseAttackCycleTicks / (double)Simulation.TicksPerSecond;
        }

        [TestCase(1)]
        [TestCase(8)]
        public void ReferenceHero_KillTimesFallInBandsPerKind(int arena)
        {
            var modules = PrototypeContent.Modules();
            var seen = new bool[EnemyArchetypes.Count + 1];
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var level = new RiftLevelSettings(16, 1, 1, 2, 0, 0, EnemyArchetypes.DepthHealthPercent(arena),
                    AllKinds(arena), playerHealth: LocationPlayerHealth);
                var seeds = RiftLevelSeeds.ForLevel(seed, arena);
                var map = new LayoutMap(modules, 64);
                level.Generate(new LayoutGenerator(), modules, map, seeds.Layout);
                var sim = new Simulation(seed, 512);
                sim.SetPlayerLevel(ReferenceCampLevel);
                var plan = level.Spawn(sim, map, seeds.Spawns);
                Assert.That(sim.Entities.MaxHealth[0], Is.EqualTo(ReferenceHealth));
                Assert.That(sim.Entities.Damage[0], Is.EqualTo(34 + 4 * Progression.DamagePerLevel));
                for (int i = 1; i < sim.Entities.Count; i++)
                {
                    var kind = sim.Entities.Kind[i];
                    seen[(int)kind] = true;
                    // Элита — строка вида без надбавки пачки.
                    Assert.That(sim.Entities.MaxHealth[i], Is.EqualTo(EnemyArchetypes.ScaleHealth(
                        sim.ArchetypeHealth(kind), EnemyArchetypes.DepthHealthPercent(arena))), kind + " seed " + seed);
                    KillBand(kind, out double min, out double max);
                    double seconds = KillSeconds(sim, i);
                    Assert.That(seconds, Is.InRange(min, max), kind + " at arena " + arena + (plan.IsElite(i) ? " (elite)" : ""));
                }
            }
            // Детёныш Расщепеня в пачки не ставится — он только из распада.
            for (int k = 1; k <= EnemyArchetypes.Count; k++)
                Assert.That(seen[k], Is.EqualTo(EnemyArchetypes.IsPlaceable((EnemyKind)k)),
                    "вид " + (EnemyKind)k + " не встал ни в одну пачку");
        }

        [Test]
        public void InterimBoss_IsGuardianWithSixThousandEightHundredScaledByDepth()
        {
            var modules = PrototypeContent.Modules();
            var level = new RiftLevelSettings(12, 1, 0, 0, 0, 0, EnemyArchetypes.DepthHealthPercent(10),
                AllKinds(10), boss: true, playerHealth: LocationPlayerHealth);
            var map = new LayoutMap(modules, 64);
            level.Generate(new LayoutGenerator(), modules, map, 42);
            var sim = new Simulation(42, 512);
            sim.SetPlayerLevel(ReferenceCampLevel);
            var plan = level.Spawn(sim, map, 43);
            int boss = plan.BossId;
            Assert.That(boss, Is.GreaterThan(0));
            Assert.That(sim.Entities.Kind[boss], Is.EqualTo(EnemyKind.ForestGuardian));
            // 6800 × 163% десятой арены.
            Assert.That(EnemyArchetypes.InterimBossHealth, Is.EqualTo(6800));
            Assert.That(sim.Entities.MaxHealth[boss], Is.EqualTo(11084));
            // Удар Хранителя ×1,25 × рост урона десятой арены (172%): 30.1.
            Assert.That(sim.Entities.Damage[boss], Is.EqualTo(30));
            Assert.That(sim.Entities.CritChance[boss], Is.EqualTo(Fix64.Zero));
            Assert.That(sim.Entities.XpReward[boss], Is.EqualTo(Progression.BossKillXp));
            // Эталонный герой одними базовыми ударами — две-три минуты; с
            // критами и способностями — в окно дока 2:30–3:15 и быстрее.
            Assert.That(KillSeconds(sim, boss), Is.InRange(120.0, 240.0));
        }
    }
}
