using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// ЦЕНА ШАГА СИМУЛЯЦИИ ПРИ ТОЛПЕ (этап 0 плана «Мобы леса v2»).
    ///
    /// Лесная арена 6 (сид 42, маршруты и поле пути строятся как в забеге),
    /// 12, 24 и 48 мобов вперемешку — рой, хранители, плюй-плод, камнекопыт,
    /// Корнехват, Расщепень, Шипомёт и Вендиго, — все сразу заметили героя.
    /// Герой бессмертный и не бьёт: ходит приказом по кругу 5 м вокруг центра
    /// поляны, чтобы стая гналась, окружала и замахивалась. 300 тиков разгона,
    /// потом 1800 замеренных Step(); медиана и p95 в микросекундах. Перед
    /// замером — холостой прогон 48 мобов, чтобы JIT успел.
    ///
    /// Порог плана: на 48 мобах p95 ≤ 1000 мкс, медиана ≤ 650; ни один поток
    /// не хуже базы больше чем на 5%. Машина шумит (параллельные сборки), поэтому
    /// каждый состав гоняется SIM_BENCH_REPEATS раз (3) и в итог идёт лучший
    /// повтор — наименее задавленный чужой нагрузкой. Хэш состояния в конце
    /// одинаков у всех повторов: поменялся он — поменялся бой, а не скорость.
    ///
    /// Это замер, а не проверка: [Explicit]. Таблица — в вывод теста и в
    /// SIM_BENCH_OUT (файл, если задан). Запуск:
    /// SIM_BENCH_OUT=... dotnet test tools/Game.Sim.Tests -c Release
    /// --filter "FullyQualifiedName~SimStepBenchmark" --logger "console;verbosity=detailed".
    /// </summary>
    public class SimStepBenchmark
    {
        private const ulong Seed = 42;
        private const int Arena = 6;
        private const int WarmupTicks = 300;
        private const int MeasuredTicks = 1800;
        private static readonly int[] Counts = { 12, 24, 48 };

        /// <summary>
        /// Состав дюжины: примерно как в поздней арене — половина роя, два
        /// хранителя, по одному тяжёлому. Двенадцатый — элита: Шипомёт в
        /// нечётной дюжине, Вендиго в чётной (на 12 — только Шипомёт).
        /// </summary>
        private static readonly EnemyKind[] Dozen =
        {
            EnemyKind.ForestRootSwarm, EnemyKind.ForestGuardian, EnemyKind.ForestRootSwarm, EnemyKind.ForestBud,
            EnemyKind.ForestRootSwarm, EnemyKind.ForestStonehoof, EnemyKind.ForestRootSwarm, EnemyKind.ForestRootSnarer,
            EnemyKind.ForestGuardian, EnemyKind.ForestSplitter, EnemyKind.ForestRootSwarm, EnemyKind.ForestThorncaster,
        };

        [Test, Explicit("Замер цены Step() на 12/24/48 мобах: медиана и p95")]
        public void MeasureStepTimeByEnemyCount()
        {
            int repeats = ArenaBalanceBench.EnvInt("SIM_BENCH_REPEATS", 3);
            var report = new StringBuilder();
            report.AppendLine("SimStepBenchmark: forest arena " + Arena + ", seed " + Seed + ", warm-up " + WarmupTicks
                + " ticks, measured " + MeasuredTicks + " ticks, best of " + repeats + " repeats"
                + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            report.AppendLine("Enemies  Median us  p95 us  Mean us  Max us  Alive  StateHash");
            // Холостой прогон: JIT дотягивает горячие методы до второго уровня, и
            // без него первый состав (12) мерил компилятор, а не шаг.
            Measure(Counts[Counts.Length - 1]);
            var detail = new StringBuilder();
            foreach (int count in Counts)
            {
                Sample best = default;
                for (int r = 0; r < repeats; r++)
                {
                    Sample sample = Measure(count);
                    detail.AppendLine("  " + count + " mobs, repeat " + (r + 1) + ": median " + F(sample.Median)
                        + ", p95 " + F(sample.P95) + ", mean " + F(sample.Mean) + ", hash " + sample.Hash.ToString("X16"));
                    if (r > 0)
                        Assert.That(sample.Hash, Is.EqualTo(best.Hash), "бой разошёлся между повторами: " + count + " мобов");
                    if (r == 0 || sample.Median < best.Median) best = sample;
                }
                report.AppendLine(count.ToString().PadLeft(7) + "  " + F(best.Median).PadLeft(9) + "  " + F(best.P95).PadLeft(6)
                    + "  " + F(best.Mean).PadLeft(7) + "  " + F(best.Max).PadLeft(6) + "  " + best.Alive.ToString().PadLeft(5)
                    + "  " + best.Hash.ToString("X16"));
            }
            report.AppendLine("Repeats:");
            report.Append(detail);
            string text = report.ToString();
            TestContext.WriteLine(text);
            Console.WriteLine(text);
            string path = Environment.GetEnvironmentVariable("SIM_BENCH_OUT");
            if (!string.IsNullOrEmpty(path))
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(path, text);
            }
        }

        private struct Sample
        {
            public double Median, P95, Mean, Max;
            public int Alive;
            public ulong Hash;
        }

        private static Sample Measure(int count)
        {
            var location = ArenaEncounterTests.ForestLocation();
            LayoutMap map = ArenaEncounterTests.ArenaMap(location, Arena, Seed);
            Assert.That(map.Routes, Is.Not.Null, "у арены нет маршрутов — поле пути не построится");
            var sim = new Simulation(Seed, 512);
            sim.SetupRift(map, Seed, 0, 0, 100);
            sim.BigAttackTokenLimit = Simulation.BigAttackTokensForArena(Arena);
            sim.BigMarkBudget = Simulation.BigMarkBudgetForArena(Arena);
            sim.PlayerInvulnerable = true;
            Fix64 heroBody = sim.Entities.BodyRadius[Simulation.PlayerId];
            FixVec2 center = map.ClampToWalkable(map.GetGlade(0).Center, heroBody);
            sim.Entities.Position[Simulation.PlayerId] = center;

            // Кольцо 6–10 м вокруг центра: каждый следующий — на золотой угол дальше.
            for (int i = 0; i < count; i++)
            {
                EnemyKind kind = Dozen[i % Dozen.Length];
                if (kind == EnemyKind.ForestThorncaster && (i / Dozen.Length) % 2 == 1) kind = EnemyKind.ForestWendigo;
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio((i * 618) % 1000, 1000);
                Fix64 radius = Fix64.FromInt(6) + Fix64.Ratio(i % 5, 1);
                FixVec2 at = map.ClampToWalkable(center + FixVec2.FromAngle(angle) * radius, Fix64.One);
                sim.AddKindTestEnemy(kind, at, 100);
            }

            var ticks = new List<double>(MeasuredTicks);
            double toMicro = 1000000.0 / Stopwatch.Frequency;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            for (int t = 0; t < WarmupTicks + MeasuredTicks; t++)
            {
                // Приказ идти по кругу: точка на 40° впереди героя на окружности 5 м.
                var input = InputFrame.Empty;
                input.Flags = (byte)InputFlags.MoveOrder;
                Fix64 lead = Fix64.TwoPi * Fix64.Ratio(t, 600) + Fix64.Ratio(7, 10);
                input.Aim = map.ClampToWalkable(center + FixVec2.FromAngle(lead) * Fix64.FromInt(5), heroBody);
                long start = Stopwatch.GetTimestamp();
                sim.Step(input);
                long spent = Stopwatch.GetTimestamp() - start;
                if (t >= WarmupTicks) ticks.Add(spent * toMicro);
            }

            var sample = new Sample { Hash = sim.StateHash() };
            for (int i = 1; i < sim.Entities.Count; i++)
                if (sim.Entities.Alive[i] && sim.Entities.Side[i] != Faction.Wole) sample.Alive++;
            double sum = 0;
            foreach (double v in ticks) sum += v;
            ticks.Sort();
            sample.Mean = sum / ticks.Count;
            sample.Median = Percentile(ticks, 0.5);
            sample.P95 = Percentile(ticks, 0.95);
            sample.Max = ticks[ticks.Count - 1];
            return sample;
        }

        private static double Percentile(List<double> sorted, double p)
        {
            double rank = p * (sorted.Count - 1);
            int low = (int)Math.Floor(rank), high = (int)Math.Ceiling(rank);
            return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
        }

        private static string F(double value) => value.ToString("0", CultureInfo.InvariantCulture);
    }
}
