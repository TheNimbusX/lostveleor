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
        public void Sqrt_IsCorrect()
        {
            Assert.AreEqual(Fix64.FromInt(2).Raw, Fix64.Sqrt(Fix64.FromInt(4)).Raw, "sqrt(4)");
            Assert.AreEqual(Fix64.FromInt(12).Raw, Fix64.Sqrt(Fix64.FromInt(144)).Raw, "sqrt(144)");
            AssertClose(Fix64.Sqrt(Fix64.FromInt(2)), 1.41421356, "sqrt(2)");
            Assert.AreEqual(0L, Fix64.Sqrt(Fix64.Zero).Raw, "sqrt(0)");
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
        public void Pcg32_IsRepeatable()
        {
            var a = new Pcg32(12345UL, 1UL);
            var b = new Pcg32(12345UL, 1UL);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), $"расхождение на шаге {i}");
        }

    }
}
