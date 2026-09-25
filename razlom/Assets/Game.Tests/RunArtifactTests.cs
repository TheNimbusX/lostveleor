using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Артефакты забега — набор акта I по списку владельца (24 сентября): один слот, живут до
    /// конца забега, активные включаются клавишей F (InputFlags.UseArtifact). Каждый тест —
    /// сцена с артефактом и без него или до и после включения. Выбор после босса проверяет
    /// MeadowCompletionTests (там нужна локация из Resources).
    /// </summary>
    public class RunArtifactTests
    {
        private static Simulation Arena(RunArtifact artifact = RunArtifact.None)
        {
            var sim = new Simulation(1234, 128);
            sim.SetupTestArena(0);
            sim.SetArtifact(artifact);
            return sim;
        }

        private static int Enemy(Simulation sim, float x, float y, int health = 10000)
        {
            int id = sim.Entities.Spawn(new FixVec2(Fix64.Ratio((int)(x * 1000), 1000), Fix64.Ratio((int)(y * 1000), 1000)),
                health, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.BodyRadius[id] = Fix64.Ratio(1, 10);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static InputFrame Press(int slot)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = new FixVec2(Fix64.FromInt(2), Fix64.Zero);
            return input;
        }

        private static InputFrame UseArtifact()
        {
            var input = InputFrame.Empty;
            input.Flags = (byte)InputFlags.UseArtifact;
            return input;
        }

        private static void Idle(Simulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Step(InputFrame.Empty);
        }

        /// <summary>Сколько здоровья герой потерял от удара врага на 100.</summary>
        private static int HitPlayer(Simulation sim, int foe, int amount = 100)
        {
            int before = sim.Entities.Health[Simulation.PlayerId];
            sim.ApplyAbilityDamage(foe, Simulation.PlayerId, amount, -1, DamageType.Physical);
            return before - sim.Entities.Health[Simulation.PlayerId];
        }

        private static int HitEnemy(Simulation sim, int foe, int amount = 100)
        {
            int before = sim.Entities.Health[foe];
            sim.ApplyAbilityDamage(Simulation.PlayerId, foe, amount, 0, DamageType.Physical);
            return before - sim.Entities.Health[foe];
        }

        [Test]
        public void TheSetIsTheOwnersEightAndRetiredNumbersAreInvalid()
        {
            Assert.AreEqual(8, RunArtifacts.Count);
            Assert.AreEqual(RunArtifact.SunSeal, RunArtifacts.At(0));
            Assert.AreEqual(RunArtifact.GuardianVow, RunArtifacts.At(RunArtifacts.Count - 1));
            for (int old = 1; old <= 3; old++) Assert.IsFalse(RunArtifacts.IsValid((RunArtifact)old), "снятый номер " + old + " всё ещё в наборе");
            Assert.IsFalse(Simulation.IsActiveArtifact(RunArtifact.GuardianVow), "Обет Хранителя — пассивный");
        }

        [Test]
        public void ActiveArtifactGoesOnCooldownAndIgnoresPressesUntilReady()
        {
            var sim = Arena(RunArtifact.SunSeal);
            sim.Step(UseArtifact());
            int ready = sim.ArtifactReadyTick;
            Assert.AreEqual(sim.Tick - 1 + Simulation.ArtifactCooldownTicks(RunArtifact.SunSeal), ready);
            Idle(sim, Simulation.SunSealTicks + 5);
            sim.Step(UseArtifact());
            Assert.AreEqual(ready, sim.ArtifactReadyTick, "повторное нажатие на перезарядке сработало");
            Assert.IsFalse(sim.ArtifactEffectActive);
        }

        [Test]
        public void SunSealMakesPelagInvulnerableForFourSeconds()
        {
            var sim = Arena(RunArtifact.SunSeal);
            int foe = Enemy(sim, 3f, 0);
            Assert.Greater(HitPlayer(sim, foe), 0);
            sim.Step(UseArtifact());
            Assert.AreEqual(0, HitPlayer(sim, foe), "под Печатью урон прошёл");
            Idle(sim, Simulation.SunSealTicks);
            Assert.Greater(HitPlayer(sim, foe), 0, "Печать не кончилась через 4 с");
        }

        [Test]
        public void ReturnDialResetsEveryCooldown()
        {
            var sim = Arena(RunArtifact.ReturnDial);
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            sim.Step(Press(0));
            Idle(sim, 5);
            Assert.Greater(sim.AbilityReadyTick(0), sim.Tick, "Вихрь не ушёл на перезарядку");
            sim.Step(UseArtifact());
            Assert.LessOrEqual(sim.AbilityReadyTick(0), sim.Tick, "Циферблат не сбросил перезарядку");
        }

        [Test]
        public void VengeanceMirrorReflectsAndSoftensDamage()
        {
            var plain = Arena(RunArtifact.VengeanceMirror);
            int a = Enemy(plain, 3f, 0);
            int taken = HitPlayer(plain, a);

            var mirrored = Arena(RunArtifact.VengeanceMirror);
            int b = Enemy(mirrored, 3f, 0);
            mirrored.Step(UseArtifact());
            int softened = HitPlayer(mirrored, b);
            Assert.Less(softened, taken, "Зеркало не уменьшило урон по герою");
            Assert.Greater(10000 - mirrored.Entities.Health[b], 0, "Зеркало не вернуло урон атакующему");
        }

        [Test]
        public void WinterHeartFreezesTheCrowdSlowsElitesAndShatters()
        {
            var sim = Arena(RunArtifact.WinterHeart);
            int crowd = Enemy(sim, 3f, 0);
            int elite = Enemy(sim, -3f, 0);
            int far = Enemy(sim, 20f, 0);
            sim.MarkElite(elite);
            sim.Entities.Stats[elite].SetBase(StatType.MoveSpeed, Fix64.FromInt(3));
            sim.Entities.RefreshStats(elite);
            float speed = sim.Entities.Stats[elite].Get(StatType.MoveSpeed).ToFloat();

            sim.Step(UseArtifact());
            Assert.IsTrue(sim.Statuses.IsStunned(crowd, sim.Tick), "обычный враг не замёрз");
            Assert.IsFalse(sim.Statuses.IsStunned(elite, sim.Tick), "элиту не должно остановить");
            Assert.Less(sim.Entities.Stats[elite].Get(StatType.MoveSpeed).ToFloat(), speed, "элиту не замедлило");
            Assert.IsFalse(sim.Statuses.IsStunned(far, sim.Tick), "замёрз враг дальше 8 м");

            int shattered = HitEnemy(sim, crowd);
            int normal = HitEnemy(sim, far);
            Assert.Greater(shattered, normal, "удар по замёрзшему не расколол лёд");
            Assert.IsFalse(sim.Statuses.IsStunned(crowd, sim.Tick), "после раскола враг остался во льду");

            Idle(sim, Simulation.WinterTicks + 2);
            Assert.AreEqual(speed, sim.Entities.Stats[elite].Get(StatType.MoveSpeed).ToFloat(), .001f, "замедление не снялось");
        }

        [Test]
        public void HourglassHoldsDamageAndReleasesItAtOnce()
        {
            var sim = Arena(RunArtifact.Hourglass);
            int foe = Enemy(sim, 3f, 0);
            sim.Step(UseArtifact());
            Assert.IsTrue(sim.TimeStopped);
            Assert.IsTrue(sim.Statuses.IsStunned(foe, sim.Tick), "враг не остановился");
            Assert.AreEqual(0, HitEnemy(sim, foe), "урон в остановленном времени прошёл сразу");
            HitEnemy(sim, foe);
            Idle(sim, Simulation.HourglassTicks + 1);
            Assert.Greater(10000 - sim.Entities.Health[foe], 150, "накопленный урон не пришёл разом");
        }

        [Test]
        public void VoidVisagePhasesAndBurstsOnEarlyExit()
        {
            var sim = Arena(RunArtifact.VoidVisage);
            sim.SetAbility(0, AbilityDefinition.Whirlwind(), new AbilityNode[0], 0);
            int foe = Enemy(sim, 1.5f, 0);
            sim.Step(UseArtifact());
            Assert.IsTrue(sim.VoidPhased);
            Assert.AreEqual(0, HitPlayer(sim, foe), "в фазе героя ударили");
            sim.Step(Press(0));
            Assert.LessOrEqual(sim.AbilityReadyTick(0), sim.Tick, "в фазе способность сработала");

            sim.Step(UseArtifact());
            Assert.IsFalse(sim.VoidPhased, "повторное нажатие не вывело из фазы");
            Assert.AreEqual(Simulation.VoidBurstDamage, 10000 - sim.Entities.Health[foe], 20, "выход без взрыва");
        }

        [Test]
        public void CrimsonHeartGrowsWithLostHealthAndBurstsAtTheEnd()
        {
            var sim = Arena(RunArtifact.CrimsonHeart);
            int attacker = Enemy(sim, 10f, 0);
            int foe = Enemy(sim, 2f, 0);
            int calm = HitEnemy(sim, foe);
            sim.Step(UseArtifact());
            int max = sim.Entities.MaxHealth[Simulation.PlayerId];
            sim.ApplyAbilityDamage(attacker, Simulation.PlayerId, max * 4 / 10, -1, DamageType.Physical);
            Assert.IsTrue(sim.Entities.Alive[Simulation.PlayerId], "удар убил героя — возьми урон меньше");
            int angry = HitEnemy(sim, foe);
            Assert.Greater(angry, calm, "Багровое Сердце не усилило урон от потерянного здоровья");

            int before = sim.Entities.Health[foe];
            Idle(sim, Simulation.CrimsonTicks + 1);
            Assert.Greater(before - sim.Entities.Health[foe], 0, "в конце не было взрыва");
        }

        [Test]
        public void GuardianVowSavesOncePerRun()
        {
            var sim = Arena(RunArtifact.GuardianVow);
            int foe = Enemy(sim, 3f, 0);
            int max = sim.Entities.MaxHealth[Simulation.PlayerId];
            sim.ApplyAbilityDamage(foe, Simulation.PlayerId, max * 10, -1, DamageType.Physical);
            Assert.IsTrue(sim.Entities.Alive[Simulation.PlayerId], "Обет не спас");
            Assert.AreEqual(max / 2, sim.Entities.Health[Simulation.PlayerId]);
            Assert.IsTrue(sim.VowUsed);
            Assert.AreEqual(0, HitPlayer(sim, foe), "после Обета нет неуязвимости");

            Idle(sim, Simulation.VowImmuneTicks + 1);
            sim.ApplyAbilityDamage(foe, Simulation.PlayerId, max * 10, -1, DamageType.Physical);
            Assert.IsFalse(sim.Entities.Alive[Simulation.PlayerId], "Обет спас второй раз");
        }

        [Test]
        public void TheRunHoldsOneArtifactAndResetsItOnANewRun()
        {
            var run = new RiftRun(new Simulation(7, 1024), PrototypeContent.Modules(),
                PrototypeContent.Items(), PrototypeContent.ItemBaseIds());
            run.StartRun();
            ulong empty = run.Hash();

            run.TakeArtifact(RunArtifact.SunSeal);
            Assert.AreEqual(RunArtifact.SunSeal, run.Artifact);
            Assert.AreEqual(RunArtifact.SunSeal, run.Sim.Artifact, "симуляция не знает об артефакте");
            Assert.AreNotEqual(empty, run.Hash(), "артефакт не входит в состояние забега");

            run.TakeArtifact(RunArtifact.GuardianVow);
            Assert.AreEqual(RunArtifact.GuardianVow, run.Artifact, "новый артефакт не заменил прежний");

            run.StartRun();
            Assert.AreEqual(RunArtifact.None, run.Artifact, "артефакт пережил конец забега");
            Assert.AreEqual(RunArtifact.None, run.Sim.Artifact);
        }
    }
}
