using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    public class Fix64Tests
    {
        private const long Tolerance = 1L << 12; // ~2.4e-7

        private static void AssertClose(Fix64 actual, double expected, string what)
        {
            long expectedRaw = Fix64.FromDouble(expected).Raw;
            long delta = actual.Raw - expectedRaw;
            if (delta < 0) delta = -delta;
            Assert.LessOrEqual(delta, Tolerance,
                $"{what}: ожидалось ~{expected}, получено {actual.ToDouble()}");
        }

        [Test]
        public void Arithmetic_Basics()
        {
            Assert.AreEqual(Fix64.One.Raw, (Fix64.One * Fix64.One).Raw, "1 * 1");
            Assert.AreEqual(Fix64.Zero.Raw, (Fix64.One - Fix64.One).Raw, "1 - 1");
            Assert.AreEqual(Fix64.FromInt(6).Raw, (Fix64.FromInt(2) * Fix64.FromInt(3)).Raw, "2 * 3");
            Assert.AreEqual(Fix64.FromInt(4).Raw, (Fix64.FromInt(12) / Fix64.FromInt(3)).Raw, "12 / 3");
            Assert.AreEqual(Fix64.Half.Raw, (Fix64.One / Fix64.FromInt(2)).Raw, "1 / 2");
        }

        [Test]
        public void Multiplication_HandlesNegatives()
        {
            Assert.AreEqual(Fix64.FromInt(-6).Raw, (Fix64.FromInt(-2) * Fix64.FromInt(3)).Raw);
            Assert.AreEqual(Fix64.FromInt(6).Raw, (Fix64.FromInt(-2) * Fix64.FromInt(-3)).Raw);
            AssertClose(Fix64.FromDouble(-1.5) * Fix64.FromDouble(2.5), -3.75, "-1.5 * 2.5");
        }

        [Test]
        public void Division_HandlesFractions()
        {
            AssertClose(Fix64.One / Fix64.FromInt(3), 1.0 / 3.0, "1/3");
            AssertClose(Fix64.FromDouble(7.5) / Fix64.FromDouble(2.5), 3.0, "7.5/2.5");
            AssertClose(Fix64.FromInt(-9) / Fix64.FromInt(4), -2.25, "-9/4");
        }

        [Test]
        public void Sqrt_IsCorrect()
        {
            Assert.AreEqual(Fix64.FromInt(2).Raw, Fix64.Sqrt(Fix64.FromInt(4)).Raw, "sqrt(4)");
            Assert.AreEqual(Fix64.FromInt(12).Raw, Fix64.Sqrt(Fix64.FromInt(144)).Raw, "sqrt(144)");
            AssertClose(Fix64.Sqrt(Fix64.FromInt(2)), 1.41421356, "sqrt(2)");
            Assert.AreEqual(0L, Fix64.Sqrt(Fix64.Zero).Raw, "sqrt(0)");
        }

        [Test]
        public void Trig_CardinalPointsAreExact()
        {
            // Синус и косинус считаются раздельными полиномами именно ради этого:
            // если бы Cos шёл через Sin, ошибка на пике синуса испортила бы cos(0),
            // а на нём стоит вся работа с направлениями.
            Assert.AreEqual(Fix64.Zero.Raw, Fix64.Sin(Fix64.Zero).Raw, "sin(0) должен быть ровно 0");
            Assert.AreEqual(Fix64.One.Raw, Fix64.Cos(Fix64.Zero).Raw, "cos(0) должен быть ровно 1");
        }

        [Test]
        public void Trig_IsAccurateEnough()
        {
            // Полиномиальное приближение, ошибка около 4e-6. Точное равенство
            // за пределами кардинальных точек не проверять никогда.
            const double Tol = 1e-4;

            Assert.That(Fix64.Sin(Fix64.PiOver2).ToDouble(), Is.EqualTo(1.0).Within(Tol), "sin(pi/2)");
            Assert.That(Fix64.Sin(-Fix64.PiOver2).ToDouble(), Is.EqualTo(-1.0).Within(Tol), "sin(-pi/2)");
            Assert.That(Fix64.Cos(Fix64.Pi).ToDouble(), Is.EqualTo(-1.0).Within(Tol), "cos(pi)");
            Assert.That(Fix64.Sin(Fix64.Pi / Fix64.FromInt(6)).ToDouble(), Is.EqualTo(0.5).Within(Tol), "sin(pi/6)");
            Assert.That(Fix64.Cos(Fix64.Pi / Fix64.FromInt(3)).ToDouble(), Is.EqualTo(0.5).Within(Tol), "cos(pi/3)");

            // Тождество sin² + cos² = 1 на всём круге.
            for (int deg = 0; deg < 360; deg += 7)
            {
                Fix64 a = Fix64.TwoPi * Fix64.Ratio(deg, 360);
                Fix64 s = Fix64.Sin(a), c = Fix64.Cos(a);
                Assert.That((s * s + c * c).ToDouble(), Is.EqualTo(1.0).Within(1e-4),
                    $"sin²+cos² на {deg}°");
            }
        }

        [Test]
        public void Atan2_QuadrantsAreCorrect()
        {
            Assert.That(Fix64.Atan2(Fix64.Zero, Fix64.One).ToDouble(),
                Is.EqualTo(0.0).Within(0.01), "atan2(0,1)");
            Assert.That(Fix64.Atan2(Fix64.One, Fix64.Zero).ToDouble(),
                Is.EqualTo(1.5708).Within(0.01), "atan2(1,0)");
            Assert.That(Fix64.Atan2(Fix64.One, Fix64.One).ToDouble(),
                Is.EqualTo(0.7854).Within(0.01), "atan2(1,1)");
            Assert.That(Fix64.Atan2(-Fix64.One, -Fix64.One).ToDouble(),
                Is.EqualTo(-2.3562).Within(0.01), "atan2(-1,-1)");
        }

        [Test]
        public void Vector_LengthAndNormalize()
        {
            var v = new FixVec2(Fix64.FromInt(3), Fix64.FromInt(4));
            AssertClose(v.Length, 5.0, "|(3,4)|");
            Assert.AreEqual(Fix64.FromInt(25).Raw, v.LengthSq.Raw, "|(3,4)|^2");

            var n = v.Normalized();
            Assert.That(n.Length.ToDouble(), Is.EqualTo(1.0).Within(0.001), "нормализованная длина");

            Assert.AreEqual(FixVec2.Zero, FixVec2.Zero.Normalized(), "нормализация нуля");
        }

        [Test]
        public void Pcg32_IsRepeatable()
        {
            var a = new Pcg32(12345UL, 1UL);
            var b = new Pcg32(12345UL, 1UL);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), $"расхождение на шаге {i}");
        }

        [Test]
        public void Pcg32_BoundedStaysInRange()
        {
            var rng = new Pcg32(7UL, 3UL);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.NextInt(5, 12);
                Assert.GreaterOrEqual(v, 5);
                Assert.Less(v, 12);
            }
        }

        [Test]
        public void NextFix_StaysInUnitRange()
        {
            var rng = new Pcg32(99UL, 4UL);
            for (int i = 0; i < 10000; i++)
            {
                Fix64 v = rng.NextFix();
                Assert.GreaterOrEqual(v.Raw, 0L);
                Assert.Less(v.Raw, Fix64.One.Raw);
            }
        }
    }
}
