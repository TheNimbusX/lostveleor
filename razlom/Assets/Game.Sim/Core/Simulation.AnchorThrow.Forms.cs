namespace Game.Sim
{
    /// <summary>
    /// ТРИ ФОРМЫ БРОСКА ЯКОРЯ (владелец 03.10, AGENTS/DESIGN.md «Пелаг — новая структура
    /// набора»): замах, полёт, натяг, возврат и ловля у всех одни — форма меняет, ЧТО
    /// задето и кто тянется.
    ///
    /// * НЕВОД — в натяг за якорем ложится сеть пены на всю длину полёта (полоса 3 м):
    ///   бьёт 50 % один раз (кто задет якорем — нет), лёгких тянет на то же полукольцо,
    ///   тяжёлых и элиту оглушает. Босс в полосе — только урон.
    /// * ВЕЕР — три полосы: якорь к курсору и два водяных призрака под ±30°; у каждой
    ///   своя дальность; урон призрака 100 %, враг задет один раз на все полосы.
    /// * ГАРПУН — якорь вонзается в первого задетого: ×2 урона, полёт кончается в точке
    ///   касания, тянется любой с весом > 0, кроме босса (Вендиго, Шипомёт, манекен —
    ///   вес 0: только ×2 и оглушение). Оглушение — вся тяга и ещё 1 с.
    ///
    /// Все числа — именованные ЗАГЛУШКИ под приёмку.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- Невод ----

        /// <summary>Полуширина сети (+ тело): полоса 3 м от руки до дальности главной полосы.</summary>
        public static readonly Fix64 AnchorThrowNetHalfWidth = Fix64.Ratio(3, 2);

        /// <summary>Урон сети — доля урона якоря: 50 % (60 → 30).</summary>
        public const int AnchorThrowNetDamagePercent = 50;

        // ---- Веер ----

        /// <summary>Поворот призраков: cos 30° и sin 30° — как «Три направления» Удара якорем.</summary>
        public static readonly Fix64 AnchorThrowFanCos = Fix64.Ratio(866, 1000);
        public static readonly Fix64 AnchorThrowFanSin = Fix64.Half;

        /// <summary>Урон призрака — доля урона якоря: 100 %.</summary>
        public const int AnchorThrowFanGhostDamagePercent = 100;

        // ---- Гарпун ----

        /// <summary>Укус Гарпуна: 200 % (60 → 120).</summary>
        public const int AnchorThrowHarpoonDamagePercent = 200;

        /// <summary>Оглушение цели Гарпуна после ловли: ещё 1 с (вся тяга — сверх него).</summary>
        public const int AnchorThrowHarpoonStunTailTicks = TicksPerSecond;

        /// <summary>Призрак Веера: dir, повёрнутый на ±30° (sign +1 — против часовой).</summary>
        private static FixVec2 AnchorThrowFanTurn(FixVec2 dir, int sign)
        {
            Fix64 s = sign > 0 ? AnchorThrowFanSin : -AnchorThrowFanSin;
            var turned = new FixVec2(dir.X * AnchorThrowFanCos - dir.Y * s, dir.X * s + dir.Y * AnchorThrowFanCos);
            return turned.Normalized();
        }

        /// <summary>Урон попадания полосы: якорь — Damage, призрак Веера — его доля.</summary>
        private static int AnchorThrowLaneDamage(AbilityBuild build, int lane)
        {
            int damage = build.Get(AbilityStatType.Damage).ToInt();
            return lane == 0 ? damage : (damage * AnchorThrowFanGhostDamagePercent + 50) / 100;
        }

        /// <summary>
        /// Невод, тик натяга: сеть ложится от руки до дальности главной полосы (стена и
        /// корпус режут) и ловит свою полосу одним разом. Задетых якорем не бьёт.
        /// Событие AnchorThrowHit (полоса 3) — по каждому, сразу за AnchorThrowYank.
        /// </summary>
        private void AnchorThrowNet(AbilityBuild build)
        {
            if (_anchorThrow.Reach0 < AnchorThrowWallMinReach) return;   // бросок в стену — никого
            int damage = (build.Get(AbilityStatType.Damage).ToInt() * AnchorThrowNetDamagePercent + 50) / 100;
            int stun = build.Get(AbilityStatType.StunTicks).ToInt();
            FixVec2 center = _anchorThrow.Center, dir = _anchorThrow.Dir;
            Fix64 from = AbordageHandReach, to = _anchorThrow.Reach0, half = _anchorThrow.NetHalfWidth;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (_throwHit[i] != 0 || !AbordageEnemy(i)) continue;
                bool touch = ThicketHullActive(i)
                    ? ThicketHullInLane(i, center + dir * from, dir, to - from, half)
                    : InsideLane(i, center, dir, from, to, half);
                if (!touch) continue;
                bool light = AbordageLight(i);
                if (!AnchorThrowHit(i, 3, Entities.Position[i], damage, light, light ? 0 : stun)) return;
            }
        }

        /// <summary>
        /// Гарпун, тик полёта k: первый задетый на отрезке (наименьшая дальность касания,
        /// ничья — индекс) — укус ×2, полёт кончается в точке касания; тянется при весе > 0,
        /// кроме босса; оглушение (натяг + R − укус) + 1 с. Никого — летит дальше. True — укус.
        /// </summary>
        private bool AnchorThrowHarpoonStep(AbilityBuild build, int lane, int k)
        {
            AnchorThrowSegment(lane, k, out Fix64 start, out Fix64 end);
            FixVec2 center = _anchorThrow.Center, dir = _anchorThrow.LaneDir(lane);
            Fix64 half = _anchorThrow.HalfWidth;
            int first = -1;
            Fix64 firstAt = end;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (_throwHit[i] != 0 || !AbordageEnemy(i)) continue;
                Fix64 at;
                if (ThicketHullActive(i))
                {
                    if (!ThicketHullInLane(i, center + dir * start, dir, end - start, half)) continue;
                    at = AnchorThrowHullContact(i, center, dir, start, end, half);
                }
                else
                {
                    if (!InsideLane(i, center, dir, start, end, half)) continue;
                    at = AnchorThrowBodyContact(Entities.Position[i], Entities.BodyRadius[i], center, dir, start, end, half);
                }
                if (first >= 0 && at >= firstAt) continue;
                first = i;
                firstAt = at;
            }
            if (first < 0) return false;

            Fix64 reach = firstAt > AbordageHandReach ? firstAt : AbordageHandReach;
            AnchorThrowSetLane(ref _anchorThrow, lane, reach, _anchorThrow.LaneStep(lane), k, AnchorThrowStop.Harpoon);
            _anchorThrow.HarpoonTarget = first;
            // Натяг — следующий тик (одна полоса), возврат — по дальности укуса.
            int stun = 1 + AnchorThrowReturnTicks(reach) + AnchorThrowHarpoonStunTailTicks;
            bool pull = Entities.Kind[first] != EnemyKind.ForestThicketMaster && Entities.PushWeight[first].Raw > 0;
            int damage = (build.Get(AbilityStatType.Damage).ToInt() * AnchorThrowHarpoonDamagePercent + 50) / 100;
            AnchorThrowHit(first, lane, center + dir * firstAt, damage, pull, stun);
            return true;
        }
    }
}
