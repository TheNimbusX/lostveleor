using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Сабля — псевдолиния PelagKit.SabreLine (план форм 02.10, тест 6): никогда не
    /// карточка способности, пустой билд не трогает StateHash (плюс зелёный
    /// CrowdStepHashPinTests), число из билда меняет сектор серии.
    /// </summary>
    public sealed class SabreLineTests
    {
        private const int Health = 1000000;

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>Стенд серии сабли, как в SabreComboTests: неподвижная мишень, герой неуязвим.</summary>
        private static Simulation Arena(out int dummy, double distance)
        {
            var sim = new Simulation(4242, 64);
            sim.SetupTestArena(0);
            var e = sim.Entities;
            e.Position[0] = FixVec2.Zero;
            e.Facing[0] = At(1, 0);
            e.PushWeight[0] = Fix64.Zero;
            e.Stats[0].SetBase(StatType.Damage, Fix64.FromInt(54));
            e.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            e.Stats[0].SetBase(StatType.LavidiumRegen, Fix64.Zero);
            sim.RefreshPlayerStats(false);
            sim.PlayerInvulnerable = true;

            dummy = e.Spawn(At(distance, 0), Health, Faction.Orvill);
            var sheet = e.Stats[dummy];
            sheet.SetBase(StatType.MaxHealth, Fix64.FromInt(Health));
            sheet.SetBase(StatType.MoveSpeed, Fix64.Zero);
            sheet.SetBase(StatType.AttackSpeed, Fix64.Zero);
            e.RefreshStats(dummy);
            e.Health[dummy] = Health;
            e.NextAttackTick[dummy] = int.MaxValue;
            e.PushWeight[dummy] = Fix64.Zero;
            e.BodyRadius[dummy] = Fix64.FromDouble(.5);
            sim.Grid.Rebuild(e);
            return sim;
        }

        private static InputFrame Hold(FixVec2 aim)
        {
            var input = InputFrame.Empty;
            input.Aim = aim;
            input.Flags = (byte)InputFlags.Attack;
            return input;
        }

        private static AbilityNode[] ReachNode(int extraMetres)
            => new[] { AbilityNode.StatMod("test.sabre.reach", AbilityStatType.Radius, ModifierOp.Flat, Fix64.FromInt(extraMetres)) };

        [Test]
        public void Sabre_IsNeverAnAbilityOrTalentCard()
        {
            for (ulong seed = 1; seed <= 60; seed++)
            {
                RiftRun run = FormBaselineScenarios.NewPrototypeRun(seed);
                run.StartRun();
                for (int screen = 0; screen < 3; screen++)
                {
                    Assert.IsTrue(FormBaselineScenarios.ClearToReward(run));
                    for (int i = 0; i < RiftRun.RewardChoices; i++)
                        Assert.AreNotEqual(PelagKit.SabreLine, run.GetOffer(i).PoolIndex, "сид " + seed);
                    FormBaselineScenarios.Choose(run, 0, screen);
                }
                for (int slot = 0; slot < RunLoadout.Slots; slot++)
                    Assert.AreNotEqual(PelagKit.SabreLine, run.Loadout.PoolIndexAt(slot));
            }
        }

        /// <summary>Пустой билд сабли (ноль узлов) — серия на константах, StateHash бит в бит.</summary>
        [Test]
        public void EmptySabreBuild_KeepsTheStateHash()
        {
            Simulation plain = Arena(out _, 1.6);
            Simulation empty = Arena(out _, 1.6);
            empty.SetBasicAttack(new AbilityNode[RunLoadout.MaxNodesPerSlot], 0);
            Assert.IsNull(empty.BasicAttackBuild);
            for (int t = 0; t < 60; t++)
            {
                plain.Step(Hold(At(3, 0)));
                empty.Step(Hold(At(3, 0)));
                Assert.AreEqual(plain.StateHash(), empty.StateHash(), "тик " + t);
            }
        }

        [Test]
        public void ActiveSabreBuild_IsHashed_AndCanBeRemoved()
        {
            Simulation plain = Arena(out _, 1.6);
            Simulation built = Arena(out _, 1.6);
            ulong before = built.StateHash();
            built.SetBasicAttack(ReachNode(1), 1);
            Assert.IsNotNull(built.BasicAttackBuild);
            Assert.AreEqual(AbilityDefinition.SabreComboId, built.BasicAttackBuild.DefinitionId);
            Assert.AreNotEqual(before, built.StateHash(), "активный билд сабли не виден хешу");
            built.SetBasicAttack(ReachNode(1), 0);
            Assert.IsNull(built.BasicAttackBuild);
            Assert.AreEqual(plain.StateHash(), built.StateHash());
        }

        /// <summary>Базовые числа определения — константы серии: билд без чисел бьёт тот же сектор.</summary>
        [Test]
        public void SabreDefinition_StartsFromTheComboConstants()
        {
            AbilityDefinition sabre = AbilityDefinition.SabreCombo();
            Assert.AreEqual(Simulation.SabreReach, sabre.GetBase(AbilityStatType.Radius));
            Assert.AreEqual(Simulation.SabreArcCos, sabre.GetBase(AbilityStatType.ArcCosine));
        }

        /// <summary>Узел-число на дальность сабли меняет сектор попадания.</summary>
        [Test]
        public void ReachNode_WidensTheSector()
        {
            // Сектор 2,5 м + тело 0,5: центр мишени на 3,2 м — вне досягаемости.
            Simulation plain = Arena(out int far, 3.2);
            for (int t = 0; t < 12; t++) plain.Step(Hold(At(3.2, 0)));
            Assert.AreEqual(Health, plain.Entities.Health[far], "мишень за краем сектора задета без узла");

            Simulation longer = Arena(out int reached, 3.2);
            longer.SetBasicAttack(ReachNode(1), 1);
            for (int t = 0; t < 12; t++) longer.Step(Hold(At(3.2, 0)));
            Assert.Less(longer.Entities.Health[reached], Health, "дальность из билда сабли не дошла до сектора");
        }
    }
}
