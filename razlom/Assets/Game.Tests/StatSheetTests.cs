using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Три слоя модификаторов — модель Path of Exile. Тесты проверяют каждый
    /// слой отдельно, все вместе, и два свойства, без которых система тихо
    /// разъедется: пересчёт только по грязному флагу и независимость итога
    /// от порядка, в котором игрок собрал билд.
    /// </summary>
    public class StatSheetTests
    {
        private const StatType S = StatType.Damage;

        private static Fix64 Pct(int percent) => Fix64.Ratio(percent, 100);

        private static StatSheet WithBase(int baseValue)
        {
            var sheet = new StatSheet();
            sheet.SetBase(S, Fix64.FromInt(baseValue));
            return sheet;
        }

        // ---- слои по отдельности ----

        [Test]
        public void Flat_AddsToBase()
        {
            var sheet = WithBase(100);
            sheet.Add(StatModifier.Flat(S, Fix64.FromInt(30), ModifierSource.Equipment, 1));
            sheet.Add(StatModifier.Flat(S, Fix64.FromInt(20), ModifierSource.Equipment, 2));

            Assert.That(sheet.Get(S), Is.EqualTo(Fix64.FromInt(150)));
        }

        // ---- все три вместе ----

        [Test]
        public void AllThreeLayers_FollowTheFormula()
        {
            var sheet = WithBase(100);
            sheet.Add(StatModifier.Flat(S, Fix64.FromInt(50), ModifierSource.Equipment, 1));
            sheet.Add(StatModifier.Increased(S, Pct(25), ModifierSource.Equipment, 2));
            sheet.Add(StatModifier.Increased(S, Pct(25), ModifierSource.TreeNode, 3));
            sheet.Add(StatModifier.More(S, Pct(50), ModifierSource.Buff, 4));
            sheet.Add(StatModifier.More(S, Pct(25), ModifierSource.RacePassive, 5));

            // (100 + 50) * (1 + 0.5) * 1.5 * 1.25 = 421.875
            Assert.That(sheet.Get(S), Is.EqualTo(Fix64.Ratio(3375, 8)));
        }

        // ---- пересчёт только по грязному флагу ----

        [Test]
        public void AddingModifier_MarksDirty_AndRecalculatesOnce()
        {
            var sheet = WithBase(100);
            sheet.Get(S);
            int before = sheet.RecalculateCount;

            sheet.Add(StatModifier.Flat(S, Fix64.FromInt(10), ModifierSource.Buff, 7));
            Assert.That(sheet.IsDirty, Is.True);

            sheet.Get(S);
            sheet.Get(S);
            sheet.Get(S);

            Assert.That(sheet.RecalculateCount, Is.EqualTo(before + 1));
        }

        [Test]
        public void RemoveSource_TakesOnlyItsOwn()
        {
            var sheet = WithBase(100);
            sheet.Add(StatModifier.Flat(S, Fix64.FromInt(10), ModifierSource.Equipment, 1));
            sheet.Add(StatModifier.Flat(S, Fix64.FromInt(20), ModifierSource.Equipment, 2));
            sheet.Add(StatModifier.Flat(S, Fix64.FromInt(40), ModifierSource.Buff, 1));

            int removed = sheet.RemoveSource(ModifierSource.Equipment, 2);

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(sheet.ModifierCount, Is.EqualTo(2));
            Assert.That(sheet.Get(S), Is.EqualTo(Fix64.FromInt(150)));
        }

        // ---- порядок сборки билда не влияет на итог ----

        [Test]
        public void Result_IsIndependentOfInsertionOrder()
        {
            // Те же значения, что в тесте выше: на них порядок точно виден.
            var mods = new[]
            {
                StatModifier.More(S, Fix64.Ratio(1, 1), ModifierSource.Equipment, 1),
                StatModifier.More(S, Fix64.Ratio(1, 2), ModifierSource.Equipment, 2),
                StatModifier.More(S, Fix64.Ratio(1, 3), ModifierSource.Equipment, 3),
            };

            // Все шесть перестановок: игрок мог надеть эти три вещи в любом порядке.
            int[][] orders =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            };

            Fix64 expected = Fix64.Zero;
            for (int o = 0; o < orders.Length; o++)
            {
                var sheet = WithBase(100);
                for (int i = 0; i < orders[o].Length; i++)
                    sheet.Add(mods[orders[o][i]]);

                Fix64 result = sheet.Get(S);
                if (o == 0) expected = result;

                Assert.That(result.Raw, Is.EqualTo(expected.Raw),
                    $"перестановка {o} дала другой итог — канонический порядок сломан");
            }
        }

    }
}
