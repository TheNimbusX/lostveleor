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
        /// Подтягивание героя к якорю.
        ///
        /// 17 тиков (0.567 с) вместо прежних 15: тяга на полсекунды читалась
        /// слишком резкой, герой проскакивал дистанцию раньше, чем глаз
        /// успевал прочитать полёт. Плюс 15% к длительности.
        /// </summary>
        public const int LeapTicks = 17;
        /// <summary>
        /// Замах Броска якоря.
        ///
        /// 18 тиков, а не 9. Внутри замаха умещается вся завязка способности:
        /// герой достаёт якорь, бросает, якорь летит до цели и втыкается — и
        /// только тогда начинается тяга. При 9 тиках на полёт якоря оставалось
        /// 0.1 секунды на пять метров: глаз не успевал, и рывок читался как
        /// телепорт без причины.
        ///
        /// Это осознанно делает старт способности медленнее. Рывок остаётся
        /// единственным способом мгновенно сменить позицию, но теперь за него
        /// платят более длинной завязкой.
        ///
        /// 15, а не 18: на 18 замах читался неестественно долгим. Полёта якоря
        /// это почти не касается — он занимает долю после выпуска, а не
        /// фиксированное время.
        /// </summary>
        public const int LeapWindupTicks = 15;

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
        public static int CastBoarding(Simulation sim, FixVec2 aim, int enemy)
        {
            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;
            FixVec2 from = e.Position[player];

            bool hooked = (uint)enemy < (uint)e.Count
                          && e.Alive[enemy]
                          && e.Side[enemy] != e.Side[player];

            FixVec2 destination = hooked ? e.Position[enemy] : aim;
            FixVec2 delta = destination - from;

            Fix64 distance = delta.Length;
            if (distance.Raw == 0) return 0;

            FixVec2 direction = delta / distance;

            // К ВРАГУ ПОДЪЕЗЖАЕМ ВПЛОТНУЮ, НО НЕ В НЕГО. Тем же отступом, что
            // и Шквал: иначе тела расталкиваются уже после прибытия и кулак
            // бьёт в пустоту, из которой цель только что выдавило.
            Fix64 reach = hooked && distance > ChainStandoff
                ? distance - ChainStandoff
                : distance;
            if (reach > LeapRange) reach = LeapRange;

            FixVec2 target = from + direction * reach;
            e.Facing[player] = direction;

            ForcedMotion.Begin(e, player, target, LeapTicks, ForcedMotionKind.Lunge);
            return LeapTicks;
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
        public static int PickChainTarget(Simulation sim, int[] scratch, Fix64 radius,
            int[] visited = null, int visitedCount = 0)
        {
            EntityStore e = sim.Entities;
            int player = Simulation.PlayerId;
            FixVec2 from = e.Position[player];

            int count = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                count = 0;
                for (int id = 1; id < e.Count; id++)
                {
                    if (!e.Alive[id] || e.Side[id] == e.Side[player]) continue;
                    if ((e.Position[id] - from).LengthSq > radius * radius) continue;
                    bool seen = false;
                    for (int v = 0; v < visitedCount; v++) if (visited[v] == id) seen = true;
                    if (pass == 0 && seen) continue;
                    scratch[count++] = id;
                }
                if (count > 0) return scratch[sim.Rng.AbilityTargets.NextInt(0, count)];
            }
            return -1;
        }

        /// <summary>Точка, куда встать при прыжке к цели: рядом, а не внутрь.</summary>
        public static FixVec2 ChainLandingSpot(EntityStore e, int target, bool crossTarget = false)
        {
            FixVec2 from = e.Position[Simulation.PlayerId];
            FixVec2 to = e.Position[target];
            FixVec2 delta = to - from;
            Fix64 distance = delta.Length;
            if (distance.Raw == 0)
            {
                FixVec2 facing = e.Facing[Simulation.PlayerId];
                if (facing.LengthSq.Raw == 0) facing = new FixVec2(Fix64.One, Fix64.Zero);
                return to + facing * ChainStandoff;
            }

            FixVec2 direction = delta / distance;
            Fix64 stop = distance > ChainStandoff ? distance - ChainStandoff : Fix64.Zero;
            return crossTarget ? to + direction * ChainStandoff : from + direction * stop;
        }
    }
}
