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

        [Test]
        public void Increased_SumsBetweenThemselves_AndMultipliesOnce()
        {
            var sheet = WithBase(100);
            sheet.Add(StatModifier.Increased(S, Pct(25), ModifierSource.Equipment, 1));
            sheet.Add(StatModifier.Increased(S, Pct(25), ModifierSource.TreeNode, 2));

            // 100 * (1 + 0.25 + 0.25) = 150, а НЕ 100 * 1.25 * 1.25 = 156.25
            Assert.That(sheet.Get(S), Is.EqualTo(Fix64.FromInt(150)));
        }

        [Test]
        public void More_MultipliesSeparately_EachWithAll()
        {
            var sheet = WithBase(100);
            sheet.Add(StatModifier.More(S, Pct(50), ModifierSource.Equipment, 1));
            sheet.Add(StatModifier.More(S, Pct(50), ModifierSource.Equipment, 2));

            // 100 * 1.5 * 1.5 = 225, а НЕ 100 * (1 + 0.5 + 0.5) = 200
            Assert.That(sheet.Get(S), Is.EqualTo(Fix64.FromInt(225)));
        }

        [Test]
        public void IncreasedAndMore_AreNotTheSameLayer()
        {
            var asIncreased = WithBase(100);
            asIncreased.Add(StatModifier.Increased(S, Pct(50), ModifierSource.Equipment, 1));
            asIncreased.Add(StatModifier.Increased(S, Pct(50), ModifierSource.Equipment, 2));

            var asMore = WithBase(100);
            asMore.Add(StatModifier.More(S, Pct(50), ModifierSource.Equipment, 1));
            asMore.Add(StatModifier.More(S, Pct(50), ModifierSource.Equipment, 2));

            // Ровно то, ради чего слои разделены: одинаковые проценты дают
            // разный итог. Слить их в один слой — потерять рычаг баланса.
            Assert.That(asIncreased.Get(S), Is.EqualTo(Fix64.FromInt(200)));
            Assert.That(asMore.Get(S), Is.EqualTo(Fix64.FromInt(225)));
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

        [Test]
        public void StatsDoNotLeakIntoEachOther()
        {
            var sheet = new StatSheet();
            sheet.SetBase(StatType.Damage, Fix64.FromInt(100));
            sheet.SetBase(StatType.Armor, Fix64.FromInt(100));
            sheet.Add(StatModifier.More(StatType.Damage, Pct(100), ModifierSource.Equipment, 1));

            Assert.That(sheet.Get(StatType.Damage), Is.EqualTo(Fix64.FromInt(200)));
            Assert.That(sheet.Get(StatType.Armor), Is.EqualTo(Fix64.FromInt(100)));
        }

        // ---- пересчёт только по грязному флагу ----

        [Test]
        public void Get_DoesNotRecalculate_WhenNothingChanged()
        {
            var sheet = WithBase(100);
            sheet.Add(StatModifier.More(S, Pct(50), ModifierSource.Equipment, 1));

            sheet.Get(S);
            int after = sheet.RecalculateCount;

            for (int i = 0; i < 1000; i++)
            {
                sheet.Get(StatType.Damage);
                sheet.Get(StatType.Armor);
                sheet.Get(StatType.MoveSpeed);
            }

            // Три тысячи чтений — ни одного пересчёта. Это и есть разница между
            // билдом с двумя сотнями модификаторов, который играется, и который
            // роняет кадры на каждой сущности.
            Assert.That(sheet.RecalculateCount, Is.EqualTo(after));
            Assert.That(sheet.IsDirty, Is.False);
        }

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
        public void SetBase_ToSameValue_DoesNotDirty()
        {
            var sheet = WithBase(100);
            sheet.Get(S);
            int before = sheet.RecalculateCount;

            sheet.SetBase(S, Fix64.FromInt(100));

            Assert.That(sheet.IsDirty, Is.False);
            Assert.That(sheet.RecalculateCount, Is.EqualTo(before));
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

        [Test]
        public void RemoveSource_Missing_DoesNotDirty()
        {
            var sheet = WithBase(100);
            sheet.Get(S);
            int before = sheet.RecalculateCount;

            Assert.That(sheet.RemoveSource(ModifierSource.Buff, 999), Is.EqualTo(0));
            Assert.That(sheet.IsDirty, Is.False);
            Assert.That(sheet.RecalculateCount, Is.EqualTo(before));
        }

        // ---- порядок сборки билда не влияет на итог ----

        [Test]
        public void Fix64Product_DependsOnOrder_SoCanonicalOrderIsRequired()
        {
            // Опорный факт, ради которого список держится отсортированным.
            // Умножение в Fix64 округляет вниз и не ассоциативно: те же три
            // сомножителя в разном порядке расходятся в младшем разряде.
            Fix64 a = Fix64.One + Fix64.Ratio(1, 1);
            Fix64 b = Fix64.One + Fix64.Ratio(1, 2);
            Fix64 c = Fix64.One + Fix64.Ratio(1, 3);

            Assert.That(((a * b) * c).Raw, Is.Not.EqualTo(((c * b) * a).Raw));
        }

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

        [Test]
        public void RemoveAndReAdd_RestoresExactSameValue()
        {
            var sheet = WithBase(100);
            sheet.Add(StatModifier.More(S, Fix64.Ratio(1, 1), ModifierSource.Equipment, 1));
            sheet.Add(StatModifier.More(S, Fix64.Ratio(1, 3), ModifierSource.Buff, 2));
            Fix64 before = sheet.Get(S);

            // Баф отвалился и повесился заново — итог обязан вернуться побитово.
            sheet.RemoveSource(ModifierSource.Buff, 2);
            sheet.Add(StatModifier.More(S, Fix64.Ratio(1, 3), ModifierSource.Buff, 2));

            Assert.That(sheet.Get(S).Raw, Is.EqualTo(before.Raw));
        }

        [Test]
        public void ManyModifiers_DoNotBreakSorting()
        {
            var sheet = WithBase(100);

            // Больше начальной вместимости — проверяем, что рост массива
            // не путает канонический порядок.
            var rng = new Pcg32(12345UL, 1UL);
            for (int i = 0; i < 200; i++)
            {
                var stat = (StatType)rng.NextInt(0, (int)StatType.Count);
                var op = (ModifierOp)rng.NextInt(0, 3);
                sheet.Add(new StatModifier(stat, op, Fix64.Ratio(1, 1 + rng.NextInt(0, 20)),
                    (ModifierSource)rng.NextInt(0, 4), rng.NextInt(0, 50)));
            }

            Assert.That(sheet.ModifierCount, Is.EqualTo(200));
            for (int i = 1; i < sheet.ModifierCount; i++)
            {
                StatModifier prev = sheet.GetModifier(i - 1);
                StatModifier curr = sheet.GetModifier(i);
                Assert.That(prev.CompareTo(in curr), Is.LessThanOrEqualTo(0),
                    $"список не отсортирован на позиции {i}");
            }
        }
    }
}
