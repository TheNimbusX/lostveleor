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
        /// Сколько тиков летит игрок. Восемь — чуть больше четверти секунды.
        ///
        /// Меньше — читается как телепорт и теряется на экране; больше —
        /// игрок успевает почувствовать, что не управляет телом. Это тот
        /// случай, когда обе границы находятся руками, а не расчётом.
        /// </summary>
        public const int LeapTicks = 8;

        // ---- Подсечка ----

        public static readonly Fix64 SweepRadius = Fix64.Ratio(65, 10);
        public const int SweepTicks = 10;

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
                Fix64 distance = delta.Length;
                FixVec2 direction = distance.Raw == 0
                    ? e.Facing[player]
                    : delta / distance;

                FixVec2 target = centre + direction * SweepGatherDistance;
                if (ForcedMotion.Begin(e, id, target, SweepTicks, ForcedMotionKind.Dragged))
                    dragged++;
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
        public static int PickChainTarget(Simulation sim, int[] scratch, int previous)
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
