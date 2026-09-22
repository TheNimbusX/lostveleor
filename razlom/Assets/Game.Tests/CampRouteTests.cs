using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Обход препятствий в лагере.
    ///
    /// Тест гоняет героя ЧЕРЕЗ НАСТОЯЩУЮ СИМУЛЯЦИЮ, а не проверяет форму пути:
    /// маршрут может быть сколь угодно красивым на бумаге и при этом не
    /// проходиться телом. Именно так и было — путь строился верно, а герой
    /// топтался на углах, и увидеть это можно было только глазами в игре.
    /// </summary>
    public class CampRouteTests
    {
        const int Size = 80;
        [Test]
        public void NarrowRightAngleDoesNotStallAtTransitCorner()
        {
            const int size=64;var cells=new bool[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                cells[y*size+x]=(x<24 && y>=20 && y<=24)||(x>=20 && x<=24 && y>=20 && y<56);
            var map=new CampWalkMap(FixVec2.Zero,Fix64.Ratio(1,8),size,size,cells);
            for(int offset=0;offset<8;offset++)
            {
                var start=new FixVec2(Fix64.One,Fix64.Ratio(325+offset,128));
                var goal=new FixVec2(Fix64.Ratio(45,16),Fix64.FromInt(6));
                var session=Session(map);session.ConfigureCampWorld(start,map);
                var route=new CampRoute(map);Assert.True(route.To(start,goal));
                Assert.Greater(Walk(session,route,goal,200),0,"offset="+offset);
            }
        }
        // Клетка 1/8 метра, начало в (-2,-2): мир занимает от -2 до 8.
        const int WallColumn = 32;   // мировой x = 2
        const int GapFrom = 40, GapTo = 44;   // мировой z ≈ 3.0 … 3.5

        static CampWalkMap Map(bool withGap)
        {
            var cells = new bool[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    cells[y * Size + x] = x != WallColumn
                                          || (withGap && y >= GapFrom && y <= GapTo);

            return new CampWalkMap(
                new FixVec2(Fix64.FromInt(-2), Fix64.FromInt(-2)),
                Fix64.Ratio(1, 8), Size, Size, cells);
        }

        static GameSession Session(CampWalkMap map)
        {
            var session = PrototypeContent.NewSession(12345UL);
            session.ConfigureCampWorld(FixVec2.Zero, map);
            return session;
        }

        /// <summary>
        /// Гоняет тики, ведя героя по маршруту ровно так же, как это делает
        /// представление. Возвращает, сколько тиков понадобилось, или -1.
        /// </summary>
        static int Walk(GameSession session, CampRoute route, FixVec2 goal, int limit)
        {
            Simulation sim = session.ActiveSim;
            for (int tick = 0; tick < limit; tick++)
            {
                FixVec2 position = sim.Entities.Position[Simulation.PlayerId];
                if (FixVec2.DistanceSq(position, goal) < Fix64.Ratio(1, 4) * Fix64.Ratio(1, 4))
                    return tick;

                var input = InputFrame.Empty;
                if (route.Advance(position, out FixVec2 aim, out bool final))
                {
                    input.Aim = aim;
                    input.Flags = CampRoute.FlagsFor(final);
                    input.AttackTarget = -1;
                }
                session.Step(input);
            }
            return -1;
        }

        [Test]
        public void RouteWalksAroundWallThroughGap()
        {
            CampWalkMap map = Map(withGap: true);
            GameSession session = Session(map);
            var goal = new FixVec2(Fix64.FromInt(5), Fix64.Zero);

            var route = new CampRoute(map);
            Assert.IsTrue(route.To(FixVec2.Zero, goal), "маршрут не проложен");

            int ticks = Walk(session, route, goal, 900);
            Assert.Greater(ticks, 0, "герой не дошёл до цели за 30 секунд");
        }

        /// <summary>
        /// Ходьба обязана ЗАКАНЧИВАТЬСЯ.
        ///
        /// Подъезд к цели шагом в четверть остатка пути не доводит до неё
        /// никогда: остаток лишь умножается на 3/4 каждый тик. Тело всё это
        /// время числится идущим, и хвост ходьбы вырождался в подползание.
        /// </summary>
        [Test]
        public void ArrivalStopsInsteadOfCreeping()
        {
            // Открытая карта: проверяется именно конец пути, а не обход.
            var cells = new bool[Size * Size];
            for (int i = 0; i < cells.Length; i++) cells[i] = true;
            var map = new CampWalkMap(
                new FixVec2(Fix64.FromInt(-2), Fix64.FromInt(-2)),
                Fix64.Ratio(1, 8), Size, Size, cells);

            GameSession session = Session(map);
            Simulation sim = session.ActiveSim;
            var goal = new FixVec2(Fix64.FromInt(5), Fix64.Zero);

            var route = new CampRoute(map);
            route.To(FixVec2.Zero, goal);

            // Пять метров на 9/2 м/с — это 34 тика хода. Вдвое с запасом на
            // разгон и торможение; всё, что дольше, и есть подползание.
            int ticks = Walk(session, route, goal, 68);
            Assert.Greater(ticks, 0, "герой не дошёл пять метров по чистому полю");

            for (int i = 0; i < 30; i++) session.Step(InputFrame.Empty);
            Assert.IsTrue(sim.Entities.Velocity[Simulation.PlayerId].LengthSq.Raw == 0,
                "герой не остановился после прибытия");
        }

    }
}
