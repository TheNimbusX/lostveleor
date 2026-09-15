using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Сборка локации из модулей.
    ///
    /// Приёмка задачи ровно две строчки: один сид — одна карта всегда,
    /// и до всех комнат можно дойти. Обе проверяются не на одном сиде,
    /// а на сотнях: генератор, который ломается на каждом сороковом сиде,
    /// на одном проверочном выглядит исправным.
    /// </summary>
    public class WorldLayoutTests
    {
        private const int MaxModules = 24;

        private static ModuleSet BuildModules()
        {
            // Вход: 4×4, выходы на все четыре стороны.
            var entrance = new ModuleDefinition("module.entrance", 4, 4, new[]
            {
                new ModuleConnector(1, 3, Direction.North),
                new ModuleConnector(3, 1, Direction.East),
                new ModuleConnector(2, 0, Direction.South),
                new ModuleConnector(0, 2, Direction.West),
            }, weight: 0, isEntrance: true);

            // Зал: 6×5, три выхода.
            var hall = new ModuleDefinition("module.hall", 6, 5, new[]
            {
                new ModuleConnector(0, 2, Direction.West),
                new ModuleConnector(5, 2, Direction.East),
                new ModuleConnector(3, 4, Direction.North),
            }, weight: 100);

            // Коридор: длинный и узкий, два выхода по торцам.
            var corridor = new ModuleDefinition("module.corridor", 5, 2, new[]
            {
                new ModuleConnector(0, 0, Direction.West),
                new ModuleConnector(4, 1, Direction.East),
            }, weight: 140);

            // Тупик: один выход. Нужен, чтобы ветки заканчивались.
            var deadEnd = new ModuleDefinition("module.dead_end", 3, 3, new[]
            {
                new ModuleConnector(0, 1, Direction.West),
            }, weight: 60);

            // Перекрёсток: четыре выхода, ветвит карту.
            var junction = new ModuleDefinition("module.junction", 4, 4, new[]
            {
                new ModuleConnector(0, 1, Direction.West),
                new ModuleConnector(3, 2, Direction.East),
                new ModuleConnector(1, 3, Direction.North),
                new ModuleConnector(2, 0, Direction.South),
            }, weight: 90);

            return new ModuleSet(new[] { entrance, hall, corridor, deadEnd, junction });
        }

        private static LayoutMap Generate(ulong seed, int target = 12)
        {
            var map = new LayoutMap(BuildModules(), MaxModules);
            new LayoutGenerator().Generate(map.Modules, seed, map, target);
            return map;
        }

        private static bool Connected(LayoutMap map)
            => map.IsConnected(new int[MaxModules], new bool[MaxModules]);

        // ---- приёмка ----

        [Test]
        public void SameSeed_GivesSameMap_Always()
        {
            const ulong seed = 0xC0FFEEUL;
            ulong expected = Generate(seed).Hash();

            for (int i = 0; i < 200; i++)
                Assert.AreEqual(expected, Generate(seed).Hash(), $"прогон {i} дал другую карту");
        }

        [Test]
        public void EveryMap_IsConnected()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                LayoutMap map = Generate(seed);

                // Порог не «больше одного», а «больше половины заказанного».
                // Генератор, который упирается на третьем модуле, прошёл бы
                // слабую проверку и выглядел бы исправным.
                Assert.That(map.PlacedCount, Is.GreaterThanOrEqualTo(6), $"сид {seed}: карта не выросла");
                Assert.That(Connected(map), Is.True, $"сид {seed}: до части комнат не дойти");
            }
        }

        [Test]
        public void ModulesNeverOverlap()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                LayoutMap map = Generate(seed);

                for (int a = 0; a < map.PlacedCount; a++)
                {
                    PlacedModule pa = map.GetPlaced(a);
                    for (int b = a + 1; b < map.PlacedCount; b++)
                    {
                        PlacedModule pb = map.GetPlaced(b);
                        Assert.That(pa.Overlaps(pb.OriginX, pb.OriginY, pb.Width, pb.Height), Is.False,
                            $"сид {seed}: модули {a} и {b} налезли друг на друга");
                    }
                }
            }
        }

        // ---- ручная расстановка тем же типом данных ----

        // ---- выходы ----

        [Test]
        public void ChosenExit_IsADeadEnd_NotTheEntrance()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                LayoutMap map = Generate(seed);

                for (int i = 0; i < map.ExitCount; i++)
                {
                    int exit = map.GetExit(i);
                    Assert.That(exit, Is.Not.EqualTo(0), $"сид {seed}: выход совпал со входом");
                    Assert.That(map.HasChild(exit), Is.False, $"сид {seed}: выход — не тупик");
                }
            }
        }

        // ---- петли ----

        // ---- необязательные ответвления с наградой ----

        [Test]
        public void RewardBranches_AreDeadEndsDistinctFromExitAndEntrance()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                LayoutMap map = Generate(seed);

                for (int i = 0; i < map.RewardBranchCount; i++)
                {
                    int branch = map.GetRewardBranch(i);
                    Assert.That(branch, Is.Not.EqualTo(0), $"сид {seed}: награда попала во вход");
                    Assert.That(map.IsExit(branch), Is.False, $"сид {seed}: награда совпала с выходом");
                    Assert.That(map.HasChild(branch), Is.False, $"сид {seed}: награда — не тупик");
                }
            }
        }

        // ---- повороты ----

        [Test]
        public void ModuleSet_RejectsDuplicateIds()
        {
            var a = new ModuleDefinition("module.same", 2, 2, new ModuleConnector[0]);
            var b = new ModuleDefinition("module.same", 3, 3, new ModuleConnector[0]);

            Assert.Throws<System.InvalidOperationException>(() => new ModuleSet(new[] { a, b }));
        }
    }
}
