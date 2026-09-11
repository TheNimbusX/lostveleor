using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Выбор ветки и состав набора.
    ///
    /// Проверяется не «таблица заполнена», а то, ради чего она существует:
    /// ветки не должны пересекаться. Набор, в котором сабля и якорь лежат
    /// вперемешку, снимает с игрока решение — а решение и есть ветка.
    /// </summary>
    public class PelagBranchTests
    {
        [Test]
        public void CampStartsOnSabre()
        {
            Assert.AreEqual(CombatBranch.Sabre, PrototypeContent.NewCamp().Branch);
        }

        [Test]
        public void SelectingBranchChangesItOnce()
        {
            var camp = PrototypeContent.NewCamp();
            Assert.IsTrue(camp.SelectBranch(CombatBranch.Anchor));
            Assert.AreEqual(CombatBranch.Anchor, camp.Branch);
            Assert.IsFalse(camp.SelectBranch(CombatBranch.Anchor), "повторный выбор — не смена");
        }

        [Test]
        public void BranchEntersCampHash()
        {
            var sabre = PrototypeContent.NewCamp();
            var anchor = PrototypeContent.NewCamp();
            anchor.SelectBranch(CombatBranch.Anchor);

            ulong a = 0UL, b = 0UL;
            sabre.HashInto(ref a);
            anchor.HashInto(ref b);
            Assert.AreNotEqual(a, b, "ветка обязана входить в состояние лагеря");
        }

        /// <summary>У обеих веток по четыре основные способности плюс общий деш.</summary>
        [Test]
        public void EveryBranchFillsEverySlot()
        {
            foreach (CombatBranch branch in new[] { CombatBranch.Sabre, CombatBranch.Anchor })
                for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                    Assert.IsNotNull(PelagKit.Definition(branch, slot), $"{branch} слот {slot}");
        }

        /// <summary>
        /// Порядок сабельных кнопок с листа владельца от 11 сентября.
        ///
        /// Пин, а не проверка логики: художник рисует иконку под номер, игрок
        /// запоминает кнопку под палец, а диздок называет третью кнопку по
        /// имени. Молчаливая перестановка слотов ломает все три сразу, и
        /// заметить её без такого теста можно только в Play.
        /// </summary>
        [Test]
        public void SabreSlotsFollowTheApprovedSheet()
        {
            Assert.AreEqual(AbilityDefinition.WhirlwindId,
                PelagKit.Definition(CombatBranch.Sabre, 0).Id, "1 — Вихрь");
            Assert.AreEqual(AbilityDefinition.CleaveId,
                PelagKit.Definition(CombatBranch.Sabre, 1).Id, "2 — Рассекающий удар");
            Assert.AreEqual(AbilityDefinition.BlazeId,
                PelagKit.Definition(CombatBranch.Sabre, 2).Id, "3 — Ладно смазал");
            Assert.AreEqual(AbilityDefinition.ChainStepId,
                PelagKit.Definition(CombatBranch.Sabre, 3).Id, "4 — Шквал");
        }

        /// <summary>
        /// Пятый слот у обеих веток — ОДИН И ТОТ ЖЕ деш. Он не принадлежит
        /// ветке и вообще не принадлежит Пелагу: это базовый инструмент
        /// передвижения, общий для всех героев.
        /// </summary>
        [Test]
        public void BothBranchesShareTheSameDash()
        {
            Assert.AreEqual(AbilityDefinition.DashId,
                PelagKit.Definition(CombatBranch.Sabre, 4).Id);
            Assert.AreEqual(AbilityDefinition.DashId,
                PelagKit.Definition(CombatBranch.Anchor, 4).Id);
        }

        /// <summary>Деш не наносит урона и не даёт неуязвимости — так в диздоке.</summary>
        [Test]
        public void DashDealsNoDamage()
        {
            Assert.AreEqual(0, AbilityDefinition.Dash().GetBase(AbilityStatType.Damage).ToInt());
        }

        /// <summary>Пустой слот действительно пуст, а не способность-пустышка.</summary>
        [Test]
        public void EmptySlotLeavesNoBuild()
        {
            var sim = new Simulation(1234, 32);
            sim.SetupTestArena(0);
            sim.SetAbility(4, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            Assert.IsNotNull(sim.GetAbility(4));

            sim.SetAbility(4, null, new AbilityNode[0], 0);
            Assert.IsNull(sim.GetAbility(4), "пустой слот оставил после себя сборку");
        }

        [Test]
        public void SlotOutsideRangeIsEmpty()
        {
            Assert.IsNull(PelagKit.Definition(CombatBranch.Sabre, -1));
            Assert.IsNull(PelagKit.Definition(CombatBranch.Sabre, Simulation.AbilitySlots));
        }

        /// <summary>
        /// Основные способности веток не пересекаются. Пятый слот исключён:
        /// деш общий по замыслу, и совпадение там — это правило, а не ошибка.
        /// </summary>
        [Test]
        public void BranchesShareNoMainAbility()
        {
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                    Assert.AreNotEqual(
                        PelagKit.Definition(CombatBranch.Sabre, a).Id,
                        PelagKit.Definition(CombatBranch.Anchor, b).Id,
                        $"сабельный слот {a} совпал с якорным {b}");
        }

        /// <summary>Внутри ветки способности тоже все разные.</summary>
        [Test]
        public void BranchHasNoDuplicates()
        {
            foreach (CombatBranch branch in new[] { CombatBranch.Sabre, CombatBranch.Anchor })
                for (int a = 0; a < Simulation.AbilitySlots; a++)
                    for (int b = a + 1; b < Simulation.AbilitySlots; b++)
                    {
                        AbilityDefinition first = PelagKit.Definition(branch, a);
                        AbilityDefinition second = PelagKit.Definition(branch, b);
                        if (first == null || second == null) continue;

                        Assert.AreNotEqual(first.Id, second.Id,
                            $"{branch}: слоты {a} и {b} — одна способность");
                    }
        }

        /// <summary>Лестница открытия слотов из диздока: 1 → 2 → 4 → 6.</summary>
        [Test]
        public void SlotsUnlockOnDesignLevels()
        {
            Assert.AreEqual(1, PelagKit.UnlockedSlots(1));
            Assert.AreEqual(2, PelagKit.UnlockedSlots(2));
            Assert.AreEqual(2, PelagKit.UnlockedSlots(3));
            Assert.AreEqual(3, PelagKit.UnlockedSlots(4));
            Assert.AreEqual(3, PelagKit.UnlockedSlots(5));
            Assert.AreEqual(4, PelagKit.UnlockedSlots(6));
        }

        /// <summary>После четвёртого слота новые кнопки не появляются никогда.</summary>
        [Test]
        public void SlotsNeverExceedFour()
        {
            for (int level = 6; level < 200; level += 7)
                Assert.AreEqual(4, PelagKit.UnlockedSlots(level));
        }
    }
}
