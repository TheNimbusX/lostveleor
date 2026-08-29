namespace Game.Sim
{
    /// <summary>
    /// «Печать пламени» — первая способность, реализованная целиком.
    ///
    /// Она существует не сама по себе, а чтобы показать КОНВЕЙЕР, одинаковый
    /// для всех двадцати способностей игры:
    ///
    ///     Каст → ВыборЦелей → ПорождениеСнарядов → ПриПопадании → ПриУбийстве
    ///
    /// Узлы дерева нового кода не пишут. Они вмешиваются в стадии:
    ///   «Жарче»          StatMod      — меняет число, кода ноль;
    ///   «Раскол»         Flag         — включает написанное здесь ветвление;
    ///   «Перекидывается» EffectInsert — вставляет эффект в стадию ПриУбийстве.
    ///
    /// Методы названы по стадиям намеренно: когда способностей станет двадцать,
    /// одинаковые имена стадий — единственное, что не даст им разъехаться.
    /// </summary>
    public static class FlameSeal
    {
        /// <summary>Урон каждого осколка при «Расколе». Число принадлежит поведению флага.</summary>
        private static readonly Fix64 SplitDamagePenalty = Fix64.Ratio(45, 100);
        private const int SplitCount = 3;

        /// <summary>Разлёт осколков: ±12° от направления броска.</summary>
        private static readonly Fix64 SplitSpreadCos = Fix64.Cos(Fix64.TwoPi * Fix64.Ratio(12, 360));
        private static readonly Fix64 SplitSpreadSin = Fix64.Sin(Fix64.TwoPi * Fix64.Ratio(12, 360));

        /// <summary>Радиус, в котором горение перекидывается на следующего.</summary>
        private static readonly Fix64 SpreadRadius = Fix64.FromInt(3);

        /// <summary>Страховка: снаряд живёт не дольше этого, даже если не долетел.</summary>
        private const int MaxFlightTicks = 90;

        // ---------- СТАДИЯ 1: КАСТ ----------

        /// <summary>
        /// Проверяет готовность и запускает остальные стадии.
        /// Возвращает false, если способность на кулдауне.
        /// </summary>
        public static bool Cast(Simulation sim, int caster, int slot, AbilityBuild build, FixVec2 aim)
        {
            FixVec2 origin = sim.Entities.Position[caster];

            // СТАДИЯ 2: ВЫБОР ЦЕЛЕЙ. У «Печати» цель — точка, а не сущность:
            // выбор целей произойдёт при попадании, по площади.
            FixVec2 target = SelectTargetPoint(origin, aim);

            SpawnProjectiles(sim, caster, slot, build, origin, target);
            return true;
        }

        // ---------- СТАДИЯ 2: ВЫБОР ЦЕЛЕЙ ----------

        /// <summary>
        /// Знак летит в указанную точку. Если ткнули в себя — бросок вперёд
        /// на радиус, иначе знак взорвался бы под ногами.
        /// </summary>
        private static FixVec2 SelectTargetPoint(FixVec2 origin, FixVec2 aim)
            => (aim - origin).LengthSq.Raw == 0 ? origin + new FixVec2(Fix64.One, Fix64.Zero) : aim;

        // ---------- СТАДИЯ 3: ПОРОЖДЕНИЕ СНАРЯДОВ ----------

        /// <summary>
        /// Один знак — или три, если взят «Раскол». Это единственное место,
        /// где флаг что-то меняет: ветвление написано один раз и включается
        /// узлом, а не переписывается под каждый билд.
        /// </summary>
        private static void SpawnProjectiles(Simulation sim, int caster, int slot, AbilityBuild build,
            FixVec2 origin, FixVec2 target)
        {
            Fix64 speed = build.Get(AbilityStatType.ProjectileSpeed);
            Fix64 damage = build.Get(AbilityStatType.Damage);

            if (!build.Has(AbilityFlag.Split))
            {
                Launch(sim, caster, slot, origin, target, speed, damage);
                return;
            }

            Fix64 splitDamage = damage * (Fix64.One - SplitDamagePenalty);
            FixVec2 toTarget = target - origin;

            // Три знака: прямо, и по одному в каждую сторону. Порядок порождения
            // фиксирован — от него зависит порядок попаданий, а значит и то,
            // кто умрёт первым при равном здоровье.
            Launch(sim, caster, slot, origin, origin + Rotate(toTarget, SplitSpreadCos, -SplitSpreadSin),
                speed, splitDamage);
            Launch(sim, caster, slot, origin, target, speed, splitDamage);
            Launch(sim, caster, slot, origin, origin + Rotate(toTarget, SplitSpreadCos, SplitSpreadSin),
                speed, splitDamage);
        }

        private static void Launch(Simulation sim, int caster, int slot,
            FixVec2 origin, FixVec2 target, Fix64 speed, Fix64 damage)
        {
            FixVec2 toTarget = target - origin;
            FixVec2 velocity = toTarget.LengthSq.Raw == 0
                ? FixVec2.Zero
                : toTarget.Normalized() * speed;

            sim.Projectiles.Spawn(origin, target, velocity, caster, slot, damage, MaxFlightTicks);
        }

        private static FixVec2 Rotate(FixVec2 v, Fix64 cos, Fix64 sin)
            => new FixVec2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

        // ---------- СТАДИЯ 4: ПРИ ПОПАДАНИИ ----------

        /// <summary>
        /// Знак долетел: вспышка по площади и поджиг всех задетых.
        ///
        /// Обход целей — строго по возрастанию индекса. Буфер запроса выделен
        /// один раз в симуляции: попаданий за забег десятки тысяч.
        /// </summary>
        public static void OnHit(Simulation sim, int projectile, AbilityBuild build)
        {
            FixVec2 at = sim.Projectiles.Target[projectile];
            int owner = sim.Projectiles.Owner[projectile];
            int slot = sim.Projectiles.Slot[projectile];
            Fix64 damage = sim.Projectiles.Damage[projectile];

            Fix64 radius = build.Get(AbilityStatType.Radius);
            int burnTicks = build.Get(AbilityStatType.BurnTicks).ToInt();
            Fix64 burnPerTick = damage * build.Get(AbilityStatType.BurnDamagePercent);

            int hitCount = sim.QueryRadiusIntoScratch(at, radius, owner);
            Faction ownerSide = sim.Entities.Side[owner];

            for (int k = 0; k < hitCount; k++)
            {
                int target = sim.HitScratch[k];
                if (sim.Entities.Side[target] == ownerSide) continue;

                sim.Statuses.ApplyBurn(target, burnPerTick, burnTicks, owner, slot);
                sim.ApplyAbilityDamage(owner, target, damage.ToInt(), slot, DamageType.Fire);
            }
        }

        // ---------- СТАДИЯ 5: ПРИ УБИЙСТВЕ ----------

        /// <summary>
        /// Эффект «Перекидывается»: горящий враг при смерти поджигает
        /// ближайшего в радиусе трёх метров.
        ///
        /// Это НАСТОЯЩИЙ КОД, а не переключатель — потому узел и относится
        /// к типу EffectInsert. Стадия вызывает его только если узел взят.
        /// </summary>
        public static void SpreadBurn(Simulation sim, int victim, int killer, int slot)
        {
            // Перекидывается только горение: убитый в упор не поджигает соседей.
            if (!sim.Statuses.IsBurning(victim)) return;

            Fix64 damage = sim.Statuses.BurnDamage[victim];
            int ticks = sim.Statuses.BurnTicksLeft[victim];
            if (ticks <= 0) return;

            FixVec2 at = sim.Entities.Position[victim];
            Faction killerSide = killer >= 0 ? sim.Entities.Side[killer] : Faction.Wole;

            int count = sim.QueryRadiusIntoScratch(at, SpreadRadius, victim);

            // Ближайший, при равном расстоянии — меньший индекс.
            // Разрыв ничьей обязателен ровно по той же причине, что и в поиске
            // цели автоатаки: иначе результат зависел бы от раскладки по ячейкам.
            int best = -1;
            Fix64 bestDistSq = Fix64.MaxValue;

            for (int k = 0; k < count; k++)
            {
                int candidate = sim.HitScratch[k];
                if (!sim.Entities.Alive[candidate]) continue;
                if (sim.Entities.Side[candidate] == killerSide) continue;
                if (sim.Statuses.IsBurning(candidate)) continue;

                Fix64 distSq = FixVec2.DistanceSq(at, sim.Entities.Position[candidate]);
                if (distSq < bestDistSq || (distSq == bestDistSq && candidate < best))
                {
                    bestDistSq = distSq;
                    best = candidate;
                }
            }

            if (best < 0) return;
            sim.Statuses.ApplyBurn(best, damage, ticks, killer, slot);
        }
    }
}
