namespace Game.Sim
{
    /// <summary>
    /// Бросок якоря: полосы и дальности (стены), превью для HUD, попадания на тике
    /// полёта, точка касания (Simulation.AnchorThrow).
    /// </summary>
    public sealed partial class Simulation
    {
        // ---------- дальность и полосы ----------

        /// <summary>
        /// Дальность полосы от центра героя: пробы 0,5 + 0,25·k (последняя — ровно length);
        /// первая непроходимая (IsWalkable(p, 0,3) — деревья и контур поляны; в лагере
        /// CanTravel) — дальность = последняя проходимая (не ближе руки). Уступы Sim не видит.
        /// Чистая функция: её же зовёт превью HUD.
        /// </summary>
        public Fix64 AnchorThrowReach(FixVec2 from, FixVec2 dir, Fix64 length)
        {
            if (length <= AbordageHandReach || (_layout == null && _campWalkMap == null)) return length;
            Fix64 last = AbordageHandReach;
            for (Fix64 at = AbordageHandReach; ; at += AnchorThrowWallStep)
            {
                bool final = at >= length;
                if (final) at = length;
                FixVec2 probe = from + dir * at;
                bool open = _campWalkMap != null ? _campWalkMap.CanTravel(from, probe) : _layout.IsWalkable(probe, AnchorThrowWallProbe);
                if (!open) return last;
                last = at;
                if (final) return length;
            }
        }

        /// <summary>
        /// План каста из точки hero по dir (ничего не пишет): форма слота, полосы (Веер —
        /// три), дальности после стен, шаги и тики полёта, полуширины полосы и сети.
        /// </summary>
        private AnchorThrowState AnchorThrowPlan(AbilityBuild build, int slot, FixVec2 hero, FixVec2 dir)
        {
            PelagForm form = FormAt(slot);
            var plan = new AnchorThrowState
            {
                Slot = slot, Form = form, HarpoonTarget = -1,
                Center = hero, Dir = dir, Origin = hero + dir * AbordageHandReach,
                Lanes = form == PelagForm.AnchorThrowFan ? 3 : 1,
                HalfWidth = build.Get(AbilityStatType.Width) / 2,
                NetHalfWidth = form == PelagForm.AnchorThrowNet ? AnchorThrowNetHalfWidth : Fix64.Zero,
            };
            if (plan.Lanes == 3)
            {
                plan.Dir1 = AnchorThrowFanTurn(dir, +1);
                plan.Dir2 = AnchorThrowFanTurn(dir, -1);
            }
            Fix64 length = build.Get(AbilityStatType.Radius);
            for (int lane = 0; lane < plan.Lanes; lane++)
            {
                Fix64 reach = AnchorThrowReach(hero, plan.LaneDir(lane), length);
                int flight = AnchorThrowFlightTicks(reach);
                Fix64 path = reach - AbordageHandReach;
                Fix64 step = path.Raw > 0 ? path / Fix64.FromInt(flight) : Fix64.Zero;
                AnchorThrowStop stop = reach < length ? AnchorThrowStop.Wall : AnchorThrowStop.Full;
                AnchorThrowSetLane(ref plan, lane, reach, step, flight, stop);
            }
            return plan;
        }

        private static void AnchorThrowSetLane(ref AnchorThrowState s, int lane, Fix64 reach, Fix64 step, int flight, AnchorThrowStop stop)
        {
            if (lane == 1) { s.Reach1 = reach; s.Step1 = step; s.Flight1 = flight; s.StopKind1 = stop; }
            else if (lane == 2) { s.Reach2 = reach; s.Step2 = step; s.Flight2 = flight; s.StopKind2 = stop; }
            else { s.Reach0 = reach; s.Step0 = step; s.Flight0 = flight; s.StopKind0 = stop; }
        }

        /// <summary>
        /// Превью для HUD (чистая функция, ничего не пишет): полосы, дальности до стен,
        /// полуширины — тем же планом, что каст. False — в слоте не Бросок якоря.
        /// «Кого заденет» и «куда упадут» — вторая очередь (AnchorThrowRing уже чистая).
        /// </summary>
        public bool AnchorThrowPreview(int slot, FixVec2 aim, out AnchorThrowState plan)
        {
            plan = new AnchorThrowState { HarpoonTarget = -1 };
            AbilityBuild build = (uint)slot < (uint)AbilitySlots ? _abilityBuilds[slot] : null;
            if (build == null || build.DefinitionId != AbilityDefinition.AnchorThrowId) return false;
            FixVec2 hero = Entities.Position[PlayerId];
            plan = AnchorThrowPlan(build, slot, hero, AnchorThrowDirection(hero, aim, Entities.Facing[PlayerId]));
            return true;
        }

        // ---------- полёт: попадания ----------

        /// <summary>
        /// Тик полёта k (1…F): каждая полоса бьёт свой отрезок. Корпус босса и укус
        /// Гарпуна обрывают полосу в точке касания — тогда натяг и ловля уточняются.
        /// </summary>
        private void SweepAnchorThrowFlight(AbilityBuild build, int k)
        {
            int serial = _anchorThrow.Serial;
            bool cut = false;
            for (int lane = 0; lane < _anchorThrow.Lanes; lane++)
            {
                if (k > _anchorThrow.LaneFlight(lane) || _anchorThrow.LaneReach(lane) < AnchorThrowWallMinReach) continue;
                bool stopped = FormIs(_anchorThrow.Slot, PelagForm.AnchorThrowHarpoon)
                    ? AnchorThrowHarpoonStep(build, lane, k)
                    : AnchorThrowSweepLane(build, lane, k);
                if (_anchorThrow.Phase == AnchorThrowPhase.None || _anchorThrow.Serial != serial) return;   // герой умер от отражения
                cut |= stopped;
            }
            if (!cut) return;
            AnchorThrowSchedule();
            _anchorThrow.PhaseEndTick = _anchorThrow.TautTick;
            AnchorThrowSetClock(_anchorThrow.CatchTick, _anchorThrow.CatchTick + AnchorThrowHoldTicks + AnchorThrowExitTicks);
        }

        /// <summary>Отрезок полосы на тике k: [0,5 + s(k−1), 0,5 + s·k], у первого — с 0; последний — до дальности.</summary>
        private void AnchorThrowSegment(int lane, int k, out Fix64 start, out Fix64 end)
        {
            Fix64 step = _anchorThrow.LaneStep(lane), reach = _anchorThrow.LaneReach(lane);
            start = k <= 1 ? Fix64.Zero : AbordageHandReach + step * (k - 1);
            end = k >= _anchorThrow.LaneFlight(lane) ? reach : AbordageHandReach + step * k;
            if (end > reach) end = reach;
            if (start > end) start = end;
        }

        /// <summary>
        /// Обычная полоса (база, Невод, Веер): задетые по индексу, каждый один раз за
        /// бросок. Корпус босса на отрезке — голова втыкается в точке касания, за ним
        /// никто. True — полоса оборвана.
        /// </summary>
        private bool AnchorThrowSweepLane(AbilityBuild build, int lane, int k)
        {
            AnchorThrowSegment(lane, k, out Fix64 start, out Fix64 end);
            FixVec2 center = _anchorThrow.Center, dir = _anchorThrow.LaneDir(lane);
            Fix64 half = _anchorThrow.HalfWidth;

            int boss = -1;
            Fix64 bossAt = end;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (_throwHit[i] != 0 || !AbordageEnemy(i) || !ThicketHullActive(i)) continue;
                if (!ThicketHullInLane(i, center + dir * start, dir, end - start, half)) continue;
                Fix64 at = AnchorThrowHullContact(i, center, dir, start, end, half);
                if (boss >= 0 && at >= bossAt) continue;
                boss = i;
                bossAt = at;
            }
            if (boss >= 0)
            {
                end = bossAt;
                Fix64 reach = bossAt > AbordageHandReach ? bossAt : AbordageHandReach;
                AnchorThrowSetLane(ref _anchorThrow, lane, reach, _anchorThrow.LaneStep(lane), k, AnchorThrowStop.Boss);
            }

            int damage = AnchorThrowLaneDamage(build, lane);
            int stun = build.Get(AbilityStatType.StunTicks).ToInt();
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (_throwHit[i] != 0 || !AbordageEnemy(i)) continue;
                Fix64 at;
                if (i == boss) at = bossAt;
                else if (ThicketHullActive(i))
                {
                    if (!ThicketHullInLane(i, center + dir * start, dir, end - start, half)) continue;
                    at = AnchorThrowHullContact(i, center, dir, start, end, half);
                }
                else
                {
                    if (!InsideLane(i, center, dir, start, end, half)) continue;
                    at = AnchorThrowBodyContact(Entities.Position[i], Entities.BodyRadius[i], center, dir, start, end, half);
                }
                bool light = AbordageLight(i);
                if (!AnchorThrowHit(i, lane, center + dir * at, damage, light, light ? 0 : stun)) return boss >= 0;
            }
            return boss >= 0;
        }

        /// <summary>
        /// Попадание: событие (Damage, Death, Stun — сразу за ним), урон, тяжёлым и элите —
        /// оглушение (босса StunByTalent пропускает). False — герой умер от отражения, всё сброшено.
        /// </summary>
        private bool AnchorThrowHit(int i, int lane, FixVec2 point, int damage, bool pull, int stunTicks)
        {
            int serial = _anchorThrow.Serial;
            _throwHit[i] = (byte)((lane + 1) | (pull ? ThrowHitPull : 0));
            _events.Add(new SimEvent(SimEventType.AnchorThrowHit, PlayerId, i, lane, pull, point,
                DamageType.Physical, DamageOrigin.Ability, serial));
            if (damage > 0) ApplyAbilityDamage(PlayerId, i, damage, _anchorThrow.Slot, DamageType.Physical);
            if (_anchorThrow.Phase == AnchorThrowPhase.None || _anchorThrow.Serial != serial) return false;
            if (stunTicks > 0 && Entities.Alive[i]) StunByTalent(i, stunTicks);
            return true;
        }

        /// <summary>Дальность вдоль оси, где фронт полосы касается круга (c, r): along − √(r² − dy²), в пределах [start, end].</summary>
        private static Fix64 AnchorThrowBodyContact(FixVec2 c, Fix64 r, FixVec2 origin, FixVec2 dir, Fix64 start, Fix64 end, Fix64 half)
        {
            FixVec2 delta = c - origin;
            Fix64 along = FixVec2.Dot(delta, dir);
            Fix64 across = Fix64.Abs(delta.X * dir.Y - delta.Y * dir.X);
            Fix64 dy = across > half ? across - half : Fix64.Zero;
            Fix64 into = dy < r ? Fix64.Sqrt(r * r - dy * dy) : Fix64.Zero;
            return Fix64.Clamp(along - into, start, end);
        }

        /// <summary>Касание корпуса босса: ближайшая точка касания по семи кругам, задевающим отрезок.</summary>
        private Fix64 AnchorThrowHullContact(int boss, FixVec2 origin, FixVec2 dir, Fix64 start, Fix64 end, Fix64 half)
        {
            Fix64 best = end;
            for (int k = 0; k < ThicketHullCircleCount; k++)
            {
                if (!TryGetThicketHullCircle(boss, k, out FixVec2 c, out Fix64 r)) break;
                FixVec2 delta = c - origin;
                Fix64 along = FixVec2.Dot(delta, dir);
                Fix64 across = Fix64.Abs(delta.X * dir.Y - delta.Y * dir.X);
                Fix64 dx = along - Fix64.Clamp(along, start, end);
                Fix64 dy = across > half ? across - half : Fix64.Zero;
                if (dx * dx + dy * dy > r * r) continue;
                Fix64 at = AnchorThrowBodyContact(c, r, origin, dir, start, end, half);
                if (at < best) best = at;
            }
            return best;
        }
    }
}
