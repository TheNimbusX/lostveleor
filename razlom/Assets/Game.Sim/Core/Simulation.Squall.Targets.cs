namespace Game.Sim
{
    /// <summary>Шквал: выбор цели, точка посадки, облёт и след пены формы (Simulation.Squall).</summary>
    public sealed partial class Simulation
    {
        // ---------- цели ----------

        /// <summary>
        /// Следующая цель из точки from. current — цель, которую только что
        /// ударили (−1 — первый прыжок после замаха).
        ///
        /// Обычный Шквал: ближайшая (по пути до посадки) непосещённая, со
        /// скидкой SquallZigzagBonus на стороне поворота, куда ведёт проводка
        /// удара (прямой — влево, обратный — вправо); сначала в конусе
        /// SquallTurnConeCos, шире — если в конусе никого. Потом посещённые,
        /// кроме текущей, тем же правилом. Последней — сама текущая (облёт).
        /// Охота — PickSquallPrey. Ничьи — младший индекс: обход по возрастанию.
        /// </summary>
        private int PickSquallTarget(AbilityBuild build, FixVec2 from, int current)
        {
            Fix64 radius = build.Get(AbilityStatType.Radius);
            FixVec2 facing = Entities.Facing[PlayerId];
            bool preferLeft = !_squall.Backhand;
            if (FormIs(_squall.Slot, PelagForm.SquallHunt)) return PickSquallPrey(radius, from, facing, preferLeft, current);

            for (int pass = 0; pass < 2; pass++)
            {
                int bestCone = -1, bestAny = -1;
                Fix64 coneScore = Fix64.Zero, anyScore = Fix64.Zero;
                for (int id = 1; id < Entities.Count; id++)
                {
                    if (id == current || !SquallInRange(id, from, radius)) continue;
                    if (SquallVisited(id) != (pass == 1)) continue;
                    Fix64 score = SquallScore(id, from, facing, preferLeft, out bool inCone);
                    if (bestAny < 0 || score < anyScore) { bestAny = id; anyScore = score; }
                    if (inCone && (bestCone < 0 || score < coneScore)) { bestCone = id; coneScore = score; }
                }
                int pick = bestCone >= 0 ? bestCone : bestAny;
                if (pick >= 0) return pick;
            }
            return current >= 0 && SquallInRange(current, from, radius) ? current : -1;
        }

        /// <summary>
        /// Охота: самая раненая (доля здоровья) в радиусе, текущая тоже — её
        /// добивают облётом. Ничья — непосещённая, потом ближе по правилу зигзага.
        /// Конус не держит: добыча решает, куда прыгать.
        /// </summary>
        private int PickSquallPrey(Fix64 radius, FixVec2 from, FixVec2 facing, bool preferLeft, int current)
        {
            int best = -1;
            bool bestSeen = false;
            Fix64 bestScore = Fix64.Zero;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (!SquallInRange(id, from, radius)) continue;
                bool seen = id == current || SquallVisited(id);
                Fix64 score = SquallScore(id, from, facing, preferLeft, out _);
                if (best >= 0)
                {
                    long mine = (long)Entities.Health[id] * Entities.MaxHealth[best];
                    long theirs = (long)Entities.Health[best] * Entities.MaxHealth[id];
                    if (mine > theirs) continue;
                    if (mine == theirs)
                    {
                        if (seen && !bestSeen) continue;
                        if (seen == bestSeen && score >= bestScore) continue;
                    }
                }
                best = id;
                bestSeen = seen;
                bestScore = score;
            }
            return best;
        }

        private bool SquallInRange(int id, FixVec2 from, Fix64 radius)
            => SquallTargetValid(id)
               && ((Entities.Position[id] - from).LengthSq <= radius * radius
                   // Хозяин Чащи — по корпусу, как цель способности (ValidAbilityTarget).
                   || ThicketHullWithin(id, from, radius));

        private bool SquallVisited(int id)
        {
            for (int v = 0; v < _chainVisitedCount; v++) if (_chainVisited[v] == id) return true;
            return false;
        }

        /// <summary>Очки цели — путь до посадки минус бонус зигзага; inCone — поворот к ней не круче 110°.</summary>
        private Fix64 SquallScore(int id, FixVec2 from, FixVec2 facing, bool preferLeft, out bool inCone)
        {
            FixVec2 delta = Entities.Position[id] - from;
            Fix64 distance = delta.Length;
            Fix64 score = distance - SquallLandingDistance(id);
            Fix64 cross = facing.X * delta.Y - facing.Y * delta.X;
            if (cross.Raw != 0 && (cross.Raw > 0) == preferLeft) score -= SquallZigzagBonus;
            inCone = facing.LengthSq.Raw == 0 || distance.Raw == 0
                     || FixVec2.Dot(facing, delta) >= SquallTurnConeCos * distance;
            return score;
        }

        // ---------- посадка ----------

        /// <summary>Посадка у цели со стороны героя: отступ SquallLandingDistance от центра, не внутрь тела.</summary>
        private FixVec2 SquallLandingSpot(int target, FixVec2 from)
        {
            FixVec2 to = Entities.Position[target];
            FixVec2 delta = to - from;
            Fix64 distance = delta.Length;
            Fix64 land = SquallLandingDistance(target);
            if (distance.Raw == 0)
            {
                FixVec2 facing = Entities.Facing[PlayerId];
                if (facing.LengthSq.Raw == 0) facing = new FixVec2(Fix64.One, Fix64.Zero);
                return to + facing * land;
            }
            Fix64 stop = distance > land ? distance - land : Fix64.Zero;
            return from + delta / distance * stop;
        }

        /// <summary>
        /// Куда прыгать к цели. Повтор той же цели — не сквозь неё, а облёт:
        /// точка на окружности посадки в 60° от нынешней, хорда почти не задевает
        /// тело. Сторона облёта выбирается раз за каст (по стороне удара) и
        /// дальше держится — герой обходит цель кругом. Обе стороны в стене — false.
        /// </summary>
        private bool TrySquallSpot(int target, FixVec2 from, bool repeat, out FixVec2 spot)
        {
            if (!repeat)
            {
                spot = SquallLandingSpot(target, from);
                return true;
            }

            FixVec2 center = Entities.Position[target];
            FixVec2 radial = from - center;
            Fix64 length = radial.Length;
            FixVec2 unit = length.Raw != 0 ? radial / length : -Entities.Facing[PlayerId];
            if (unit.LengthSq.Raw == 0) unit = new FixVec2(Fix64.One, Fix64.Zero);
            Fix64 land = SquallLandingDistance(target);

            int sign = _squall.OrbitSign;
            if (sign == 0)
            {
                FixVec2 facing = Entities.Facing[PlayerId];
                FixVec2 ccw = center + SquallOrbitTurn(unit, 1) * land - from;
                Fix64 cross = facing.X * ccw.Y - facing.Y * ccw.X;
                sign = (cross.Raw > 0) == !_squall.Backhand ? 1 : -1;
            }
            for (int attempt = 0; attempt < 2; attempt++, sign = -sign)
            {
                FixVec2 candidate = center + SquallOrbitTurn(unit, sign) * land;
                if (!SquallCanTravel(from, candidate)) continue;
                _squall.OrbitSign = sign;
                spot = candidate;
                return true;
            }
            spot = from;
            return false;
        }

        private static FixVec2 SquallOrbitTurn(FixVec2 v, int sign)
        {
            Fix64 s = sign > 0 ? SquallOrbitSin : -SquallOrbitSin;
            return new FixVec2(v.X * SquallOrbitCos - v.Y * s, v.X * s + v.Y * SquallOrbitCos);
        }
    }
}
