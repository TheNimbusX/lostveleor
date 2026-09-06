namespace Game.Sim
{
    /// <summary>
    /// Три способности Пелага на якоре и цепи.
    ///
    /// Собраны в один файл намеренно: это не три механики, а три фасада одной.
    /// Все три сводятся к «за N тиков переставить тело из точки A в точку B»
    /// через <see cref="ForcedMotion"/>, и различаются только тем, кого
    /// двигают и по какому правилу выбирают цель.
    ///
    /// * Бросок якоря — тянет ИГРОКА к точке. Вход в бой.
    /// * Подсечка — тянет ВРАГОВ к игроку. Сбор толпы.
    /// * Шаг по цепи — тянет игрока по цепочке врагов. Выход из окружения.
    ///
    /// ЧТО ДЕЛАЕТ ЭТОТ КИТ ОСОБЕННЫМ. Ни одна из трёх не наносит урон первой
    /// задачей: они меняют РАСПОЛОЖЕНИЕ. Урон — побочный эффект перемещения.
    /// Отсюда и фантазия персонажа: он не подходит и бьёт, он стягивает.
    /// </summary>
    public static class AnchorKit
    {
        // ---- Бросок якоря ----

        /// <summary>Дальность броска. Дальше цепь не достаёт.</summary>
        public static readonly Fix64 LeapRange = Fix64.FromInt(7);

        /// <summary>
        /// Подтягивание длится 0.5 секунды: читаемый бросок без затянутого зависания.
        /// </summary>
        public const int LeapTicks = 15;
        public const int LeapWindupTicks = 9;

        // ---- Подсечка ----

        public static readonly Fix64 SweepRadius = Fix64.Ratio(65, 10);
        public const int SweepTicks = 9;
        public const int SweepCastDelayTicks = 13;
        public const float SweepAngleDegrees = 45f;

        public static bool InSweepCone(FixVec2 delta, FixVec2 facing)
        {
            Fix64 dot = FixVec2.Dot(delta, facing);
            return dot.Raw >= 0 && dot * dot >= delta.LengthSq * Fix64.Ratio(853554, 1000000);
        }

        /// <summary>
        /// Куда именно волочит. НЕ в самого игрока, а на это расстояние перед
        /// ним: втащить толпу внутрь собственного тела значит устроить давку,
        /// из которой расталкивание будет выпутываться полсекунды.
        /// </summary>
        public static readonly Fix64 SweepGatherDistance = Fix64.Ratio(13, 10);

        public const int SweepMaxTargets = 10;

        // ---- Шаг по цепи ----

        public static readonly Fix64 ChainRange = Fix64.Ratio(55, 10);
        public const int ChainMaxHops = 4;
        public const int ChainTicksPerHop = 5;

        /// <summary>Куда встать относительно цели прыжка: не в неё, а рядом.</summary>
        public static readonly Fix64 ChainStandoff = Fix64.Ratio(9, 10);

        /// <summary>
        /// БРОСОК ЯКОРЯ. Швыряет якорь в точку прицела и подтягивает туда себя.
        ///
        /// Точка ограничивается дальностью цепи, а не отменяется: клик за
        /// пределом даёт бросок на максимум в ту же сторону. Отменять было бы
        /// честнее формально и хуже на практике — игрок целится примерно.
        /// </summary>
        public static void CastLeap(Simulation sim, FixVec2 aim)
        {
            EntityStore e = sim.Entities;
            FixVec2 from = e.Position[Simulation.PlayerId];
            FixVec2 delta = aim - from;

            Fix64 distance = delta.Length;
            if (distance.Raw == 0) return;

            FixVec2 direction = delta / distance;
            Fix64 reach = distance > LeapRange ? LeapRange : distance;
            FixVec2 target = from + direction * reach;
            e.Facing[Simulation.PlayerId] = direction;

            ForcedMotion.Begin(e, Simulation.PlayerId, target, LeapTicks,
                ForcedMotionKind.Lunge);
        }

        /// <summary>
        /// ПОДСЕЧКА. Якорь уходит за спины врагов дугой, рывок цепи волочит их
        /// к игроку.
        ///
        /// Тяжёлые не поддаются — это решает <see cref="ForcedMotion"/> по
        /// весу тела, и решает ОДИНАКОВО для крюка и для толпы: враг, которого
        /// не сдвинуть плечом, не сдвигается и цепью.
        ///
        /// Возвращает, скольких утащило. Ноль — законный результат: вокруг
        /// были только тяжёлые, и это игрок обязан увидеть.
        /// </summary>
        public static int CastSweep(Simulation sim, int[] scratch)
        {
            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;
            FixVec2 centre = e.Position[player];

            int found = sim.Grid.QueryRadius(e, centre, SweepRadius, player, scratch);
            int dragged = 0;

            for (int k = 0; k < found && dragged < SweepMaxTargets; k++)
            {
                int id = scratch[k];
                if (!e.Alive[id]) continue;
                if (e.Side[id] == e.Side[player]) continue;

                // Сбор в кольцо перед игроком, а не в его тело: направление
                // берётся от игрока к цели, то есть каждый приезжает со своей
                // стороны и они не сходятся в одну точку.
                FixVec2 delta = e.Position[id] - centre;
                if (!InSweepCone(delta, sim.SweepDirection)) continue;
                Fix64 distance = delta.Length;
                FixVec2 direction = distance.Raw == 0
                    ? e.Facing[player]
                    : delta / distance;

                FixVec2 target = centre + direction * SweepGatherDistance;
                if (ForcedMotion.Begin(e, id, target, SweepTicks, ForcedMotionKind.Dragged))
                    scratch[dragged++] = id;
            }

            return dragged;
        }

        /// <summary>
        /// ШАГ ПО ЦЕПИ. Серия прыжков от врага к врагу.
        ///
        /// Здесь считается только ПЕРВЫЙ прыжок: остальные назначаются по мере
        /// прибытия, в <see cref="Simulation"/>. Причина в том, что цепочка,
        /// посчитанная вперёд, к третьему прыжку упирается в трупы — цели
        /// умирают по дороге от ударов той же способности.
        ///
        /// Возвращает выбранную цель или -1, если рядом никого.
        /// </summary>
        public static int PickChainTarget(Simulation sim, int[] scratch, int previous,
            int[] visited = null, int visitedCount = 0)
        {
            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;
            FixVec2 from = e.Position[player];

            int found = sim.Grid.QueryRadius(e, from, ChainRange, player, scratch);

            int best = -1;
            Fix64 bestDistanceSq = Fix64.Zero;
            for (int k = 0; k < found; k++)
            {
                int id = scratch[k];
                if (id == previous) continue;
                bool alreadyHit = false;
                for (int v = 0; v < visitedCount; v++)
                    if (visited[v] == id) { alreadyHit = true; break; }
                if (alreadyHit) continue;
                if (!e.Alive[id]) continue;
                if (e.Side[id] == e.Side[player]) continue;

                Fix64 distanceSq = (e.Position[id] - from).LengthSq;

                // Ближайший, при равенстве — меньший индекс. Обход идёт по
                // возрастанию, поэтому строгое сравнение уже даёт меньший.
                if (best < 0 || distanceSq < bestDistanceSq)
                {
                    best = id;
                    bestDistanceSq = distanceSq;
                }
            }

            return best;
        }

        /// <summary>Точка, куда встать при прыжке к цели: рядом, а не внутрь.</summary>
        public static FixVec2 ChainLandingSpot(EntityStore e, int target)
        {
            FixVec2 from = e.Position[Simulation.PlayerId];
            FixVec2 to = e.Position[target];
            FixVec2 delta = to - from;
            Fix64 distance = delta.Length;
            if (distance.Raw == 0) return to;

            FixVec2 direction = delta / distance;
            Fix64 stop = distance > ChainStandoff ? distance - ChainStandoff : Fix64.Zero;
            return from + direction * stop;
        }
    }
}
