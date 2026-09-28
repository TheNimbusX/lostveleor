using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Кислые лужи гнилого плода Плюй-плода (просьба владельца от 26.09): каждый
    /// третий залп — гнилой четвёртый плод, лужа зреет 9 тиков, жжёт раз в 15,
    /// перекрытые лужи не жгут дважды, луж не больше четырёх, лужа переживает стрелка.
    /// </summary>
    public class ForestPuddleTests
    {
        private static Simulation Arena()
        {
            var sim = new Simulation(123, 64);
            sim.SetupForestBudEncounter(null, 123, 1);
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.RefreshStats(0);
            sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            sim.Entities.NextAttackTick[1] = 0;
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(1);
            return sim;
        }

        private static List<ForestFruitState> Launched(Simulation sim)
        {
            var list = new List<ForestFruitState>();
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.ForestFruitLaunched && sim.TryGetForestFruit(e.Amount, out var fruit)) list.Add(fruit);
            return list;
        }

        [Test]
        public void EveryThirdVolley_ItsFourthFruitIsRotten()
        {
            var sim = Arena();
            int volleys = 0;
            var rottenVolleys = new List<int>();
            for (int t = 0; t < 1600 && volleys < 7; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events) if (e.Type == SimEventType.ForestBudVolleyStarted) volleys++;
                foreach (var fruit in Launched(sim))
                {
                    if (!fruit.Rotten) continue;
                    Assert.AreEqual(Simulation.RottenShotIndex, fruit.ShotIndex, "гнилой — четвёртый плод");
                    Assert.AreEqual(Simulation.RottenFruitRadius, fruit.Radius, "диск гнилого шире");
                    rottenVolleys.Add(volleys);
                }
            }
            Assert.AreEqual(7, volleys);
            Assert.AreEqual(new[] { 3, 6 }, rottenVolleys.ToArray(), "гнилой — в каждом третьем залпе");
        }

        [Test]
        public void Puddle_ArmsAfterNineTicks_AndBurnsEveryFifteen_OnlyInside()
        {
            var sim = Arena();
            ForestPuddleState puddle = default;
            int opened = -1;
            for (int t = 0; t < 1400 && opened < 0; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.PuddleOpened) { opened = sim.Tick - 1; sim.TryGetForestPuddle(e.Amount, out puddle); }
            }
            Assert.That(opened, Is.GreaterThan(0), "лужа так и не легла");
            Assert.AreEqual(opened + Simulation.PuddleArmTicks, puddle.ArmTick);
            Assert.AreEqual(puddle.ArmTick + Simulation.PuddleLifeTicks, puddle.EndTick);
            Assert.AreEqual(Simulation.PuddleDamageOf(sim.Entities.Damage[1]), puddle.Damage);

            // Герой — в центре лужи, стрелок больше не стреляет.
            sim.Entities.Position[0] = puddle.Center;
            sim.Statuses.ApplyStun(1, 100000);
            var burns = new List<int>();
            while (sim.Tick < puddle.EndTick + 20)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.DamageOverTime && e.Target == 0 && e.Source == 1) burns.Add(sim.Tick - 1);
            }
            Assert.That(burns.Count, Is.EqualTo((Simulation.PuddleLifeTicks - 1) / Simulation.PuddlePulseTicks), "тиков кислоты за жизнь лужи");
            Assert.AreEqual(puddle.ArmTick + Simulation.PuddlePulseTicks, burns[0], "первый тик — через 15 после созревания");
            for (int i = 1; i < burns.Count; i++) Assert.AreEqual(Simulation.PuddlePulseTicks, burns[i] - burns[i - 1]);
            Assert.That(burns.Count * puddle.Damage, Is.LessThanOrEqualTo(30), "вся лужа — не больше 30 здоровья");

            // Снаружи кромки — ни тика.
            var outside = Arena();
            ForestPuddleState second = default;
            for (int t = 0; t < 1400 && second.Serial == 0; t++)
            {
                outside.Step(InputFrame.Empty);
                foreach (var e in outside.Events) if (e.Type == SimEventType.PuddleOpened) outside.TryGetForestPuddle(e.Amount, out second);
            }
            outside.Entities.Position[0] = second.Center + new FixVec2(Simulation.PuddleHitRadius + Fix64.Ratio(1, 10), Fix64.Zero);
            outside.Statuses.ApplyStun(1, 100000);
            int outsideBurns = 0;
            while (outside.Tick < second.EndTick + 5)
            {
                outside.Step(InputFrame.Empty);
                foreach (var e in outside.Events) if (e.Type == SimEventType.DamageOverTime && e.Target == 0) outsideBurns++;
            }
            Assert.AreEqual(0, outsideBurns, "за кромкой лужи кислоты нет");
        }

        [Test]
        public void PuddleOutlivesItsBud_AndTheHourglassFreezesIt()
        {
            var sim = Arena();
            ForestPuddleState puddle = default;
            int slot = -1;
            for (int t = 0; t < 1400 && slot < 0; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events) if (e.Type == SimEventType.PuddleOpened) { slot = e.Amount; sim.TryGetForestPuddle(slot, out puddle); }
            }
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            sim.Entities.Alive[1] = false;
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetForestPuddle(slot, out var still), "лужа пережила стрелка");
            Assert.AreEqual(puddle.Serial, still.Serial);
        }

        [TestCase(1.44, true)]
        [TestCase(1.46, false)]
        public void SmallerPuddleBurnsOnlyWithinItsEdgePlusHeroMargin(double distance, bool shouldBurn)
        {
            var sim = Arena();
            ForestPuddleState puddle = default;
            for (int tick = 0; tick < 1400 && puddle.Serial == 0; tick++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.PuddleOpened) sim.TryGetForestPuddle(e.Amount, out puddle);
            }
            Assert.That(puddle.Serial, Is.GreaterThan(0), "лужа так и не появилась");
            Assert.That(puddle.Radius.ToDouble(), Is.EqualTo(1.2).Within(1e-6));
            sim.Entities.Position[0] = puddle.Center + new FixVec2(Fix64.FromDouble(distance), Fix64.Zero);
            sim.Statuses.ApplyStun(1, 100000);
            int burns = 0;
            while (sim.Tick < puddle.EndTick)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.DamageOverTime && e.Target == 0) burns++;
            }
            Assert.That(burns, Is.EqualTo(shouldBurn ? 6 : 0), "граница урона должна быть 1,45 м");
        }

        [Test]
        public void PuddleIsAvoidedByMobs_AndWeighsInTheMarkBudget()
        {
            var sim = Arena();
            ForestPuddleState puddle = default;
            for (int t = 0; t < 1400 && puddle.Serial == 0; t++)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events) if (e.Type == SimEventType.PuddleOpened) sim.TryGetForestPuddle(e.Amount, out puddle);
            }
            sim.Statuses.ApplyStun(1, 100000);
            sim.Entities.Position[0] = puddle.Center + new FixVec2(Fix64.FromInt(3), Fix64.Zero);
            int load = sim.BigMarkLoad(out _);
            Assert.That(load, Is.GreaterThanOrEqualTo(1), "лужа у героя — в бюджете меток");
            // Хранитель посреди лужи из неё уходит.
            int guardian = sim.SpawnEnemy(puddle.Center, 5000, EnemyKind.ForestGuardian);
            sim.Entities.Aggro[guardian] = true;
            sim.Entities.NextAttackTick[guardian] = int.MaxValue;
            sim.Grid.Rebuild(sim.Entities);
            for (int t = 0; t < 30; t++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(sim.Entities.Position[guardian], puddle.Center).ToDouble(),
                Is.GreaterThan(Simulation.PuddleRadius.ToDouble()), "моб стоит в кислоте");
        }

        [Test]
        public void SameSeedSamePuddles()
        {
            var a = Arena(); var b = Arena();
            for (int t = 0; t < 900; t++)
            {
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                Assert.AreEqual(a.StateHash(), b.StateHash(), "tick " + t);
            }
        }
    }
}
