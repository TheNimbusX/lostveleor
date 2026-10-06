namespace Game.Sim
{
    /// <summary>Абордаж: годность и выбор цели, точка посадки, точка укуса, досягаемость удара (Simulation.Abordage).</summary>
    public sealed partial class Simulation
    {
        // ---------- выбор цели ----------

        /// <summary>
        /// Чистое правило выбора цели Абордажа (ничего не пишет — HUD зовёт между
        /// тиками). Мышь им НЕ пользуется: каст идёт только по врагу под курсором
        /// (InputFrame.AbilityTarget, ValidAbilityTarget), как у Шквала (владелец
        /// 02.10 ~23:00). Правило оставлено под будущий мягкий захват геймпада
        /// (aim = герой + стик × R) и под перевыбор цели, пропавшей до зацепа.
        ///
        /// 1. наведённый (hovered), если годен;
        /// 2. ближайший к aim по краю тела, не дальше AbordageCursorReach (босс — по корпусу);
        /// 3. конус ±30° от from к aim: меньше «поперёк + 0,25 × вдоль» — лучше.
        /// Годен: живой враг, не в нырке, в дальности сборки (босс — по корпусу),
        /// путь до точки посадки проходим. Никого — −1. Ничьи — младший индекс.
        /// </summary>
        public int PickAbordageTarget(int hovered, FixVec2 aim, FixVec2 from, AbilityBuild build)
        {
            if (build == null) return -1;
            if (AbordageCandidate(hovered, from, build)) return hovered;
            int near = AbordageNearest(aim, from, build, -1);
            if (near >= 0) return near;

            FixVec2 ray = aim - from;
            if (ray.LengthSq.Raw == 0) ray = Entities.Facing[PlayerId];
            if (ray.LengthSq.Raw == 0) return -1;
            FixVec2 unit = ray.Normalized();
            int best = -1;
            Fix64 bestScore = Fix64.Zero;
            for (int id = PlayerId + 1; id < Entities.Count; id++)
            {
                if (!AbordageCandidate(id, from, build)) continue;
                FixVec2 delta = Entities.Position[id] - from;
                Fix64 distance = delta.Length;
                Fix64 along = FixVec2.Dot(delta, unit);
                if (distance.Raw != 0 && along < AbordageConeCos * distance) continue;
                Fix64 across = Fix64.Abs(unit.X * delta.Y - unit.Y * delta.X);
                Fix64 score = across + along * AbordageConeAlongWeight;
                if (best >= 0 && score >= bestScore) continue;
                best = id;
                bestScore = score;
            }
            return best;
        }

        /// <summary>Правило 2: ближайший к точке по краю тела (босс — по корпусу), не дальше AbordageCursorReach.</summary>
        private int AbordageNearest(FixVec2 point, FixVec2 from, AbilityBuild build, int exclude)
        {
            int best = -1;
            Fix64 bestGap = Fix64.Zero;
            for (int id = PlayerId + 1; id < Entities.Count; id++)
            {
                if (id == exclude || !AbordageCandidate(id, from, build)) continue;
                // ThicketHullGap: у босса — до корпуса, у прочих — расстояние до центра минус тело.
                Fix64 gap = ThicketHullGap(id, point);
                if (gap > AbordageCursorReach) continue;
                if (best >= 0 && gap >= bestGap) continue;
                best = id;
                bestGap = gap;
            }
            return best;
        }

        /// <summary>Годная цель из точки from: живой враг, не в нырке, в дальности сборки, путь до посадки проходим.</summary>
        private bool AbordageCandidate(int id, FixVec2 from, AbilityBuild build)
        {
            if (build == null || !SquallTargetValid(id)) return false;
            Fix64 range = build.Get(AbilityStatType.Radius);
            bool inRange = (Entities.Position[id] - from).LengthSq <= range * range || ThicketHullWithin(id, from, range);
            return inRange && AbordagePathClear(id, from);
        }

        /// <summary>Путь телом героя до точки посадки проходим (стены, уступы арены). Без карты — всегда.</summary>
        private bool AbordagePathClear(int target, FixVec2 from)
            => SquallCanTravel(from, AbordageLandingSpot(target, from));

        // ---------- посадка и укус ----------

        /// <summary>
        /// Точка посадки у цели со стороны from: вплотную, радиусы тел + 0,1 м
        /// (SquallLandingDistance). Хозяин Чащи — точка входа луча from → центр в
        /// корпус, раздутый на тело героя + 0,1: у груди, не внутри. from уже у
        /// корпуса — сам from (тяги нет).
        /// </summary>
        public FixVec2 AbordageLandingSpot(int target, FixVec2 from)
        {
            if ((uint)target >= (uint)Entities.Count) return from;
            if (ThicketHullActive(target)
                && AbordageHullEntry(target, from, Entities.BodyRadius[PlayerId] + SquallLandingGap, out FixVec2 entry))
                return entry;
            return SquallLandingSpot(target, from);
        }

        /// <summary>Точка укуса якоря: край тела цели со стороны from (у босса — край корпуса).</summary>
        private FixVec2 AbordageBitePoint(int target, FixVec2 from)
        {
            if (ThicketHullActive(target) && AbordageHullEntry(target, from, Fix64.Zero, out FixVec2 entry)) return entry;
            FixVec2 center = Entities.Position[target];
            FixVec2 delta = center - from;
            Fix64 distance = delta.Length;
            if (distance.Raw == 0) return center;
            Fix64 r = Fix64.Min(Entities.BodyRadius[target], distance);
            return center - delta / distance * r;
        }

        /// <summary>
        /// Первая точка луча from → центр босса, где тело радиуса body касается
        /// корпуса (семь кругов, Simulation.ForestBoss.Hull). from уже внутри — from.
        /// </summary>
        private bool AbordageHullEntry(int boss, FixVec2 from, Fix64 body, out FixVec2 entry)
        {
            entry = from;
            FixVec2 delta = Entities.Position[boss] - from;
            Fix64 length = delta.Length;
            if (length.Raw == 0) return true;
            FixVec2 d = delta / length;
            Fix64 first = length;
            bool hit = false;
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                if (!TryGetThicketHullCircle(boss, k, out FixVec2 center, out Fix64 radius)) return false;
                Fix64 r = radius + body;
                FixVec2 rel = from - center;
                Fix64 b = FixVec2.Dot(d, rel);
                Fix64 disc = b * b - (rel.LengthSq - r * r);
                if (disc.Raw <= 0) continue;
                Fix64 root = Fix64.Sqrt(disc);
                Fix64 enter = -b - root, leave = -b + root;
                if (leave.Raw <= 0) continue;
                if (enter.Raw < 0) enter = Fix64.Zero;
                if (!hit || enter < first) { first = enter; hit = true; }
            }
            if (hit) entry = from + d * first;
            return hit;
        }

        // ---------- удар ----------

        /// <summary>
        /// Кулак достаёт: зазор не больше посадки + 0,9 и между телами нет стены
        /// (правило Шквала, SquallContactReachable). Босс — зазор до корпуса.
        /// </summary>
        private bool AbordageContactReachable(int target)
        {
            if (!ThicketHullActive(target)) return SquallContactReachable(target);
            Fix64 gap = ThicketHullGap(target, Entities.Position[PlayerId]) - Entities.BodyRadius[PlayerId];
            return gap <= SquallLandingGap + SquallContactSlack;
        }

        /// <summary>Цель умерла в тяге: кулак бьёт ближайшего в той же досягаемости, иначе воздух (−1).</summary>
        private int AbordageNearestInReach()
        {
            FixVec2 at = Entities.Position[PlayerId];
            int best = -1;
            Fix64 bestGap = Fix64.Zero;
            for (int id = PlayerId + 1; id < Entities.Count; id++)
            {
                if (!SquallTargetValid(id) || !AbordageContactReachable(id)) continue;
                Fix64 gap = ThicketHullGap(id, at);
                if (best >= 0 && gap >= bestGap) continue;
                best = id;
                bestGap = gap;
            }
            return best;
        }

        /// <summary>Цель пропала до зацепа: ближайшая годная в AbordageCursorReach от её места; нет — −1.</summary>
        private int AbordageRetarget(AbilityBuild build, int lost)
        {
            FixVec2 place = (uint)lost < (uint)Entities.Count ? Entities.Position[lost] : Entities.Position[PlayerId];
            return AbordageNearest(place, Entities.Position[PlayerId], build, lost);
        }
    }
}
