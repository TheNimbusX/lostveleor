namespace Game.Sim
{
    /// <summary>Удары Крушения: махи, удар оземь с кругом и валом, «Четвёртый удар» (Simulation.Wreck).</summary>
    public sealed partial class Simulation
    {
        private byte[] _wreckWaveHits;   // 1 — задет кругом удара, 2 — его уже прошёл вал (стена)
        private bool[] _wreckCarried;    // Волнорез: несёт стена

        /// <summary>
        /// Удар этапа в тик контакта. WreckStage — до урона этого тика (Damage, Death, Stun
        /// следом); потом окно, кулдаун (от последнего удара серии) и часы. Махи бьют по ходу
        /// головы (Simulation.Wreck.Sweep): справа — до контакта, в тик контакта — после WreckStage.
        /// </summary>
        private void WreckStrike(AbilityBuild build)
        {
            int stage = _wreck.Stage, slot = _wreck.Slot, serial = _wreck.Serial;
            int count = WreckStageCount(build);
            _wreck.Strikes = stage + 1;
            bool last = _wreck.Strikes >= count;
            _events.Add(new SimEvent(SimEventType.WreckStage, PlayerId, -1, stage, last, Entities.Position[PlayerId]));

            if (stage == WreckStages - 1) WreckSlamHit(build);
            else if (stage >= WreckStages) WreckFourthHit(build);
            // Герой умер от отражения — серия сброшена вместе с расстановкой способностей.
            if (_wreck.Serial != serial || _wreck.Phase == WreckPhase.None) return;

            // «Серия окупается»: возврат за последний удар, сколько бы ударов в серии ни было.
            if (last && build.Has(AbilityFlag.WreckRefund)) RefundLavidium(WreckRefundAmount);
            _wreck.WindowEndTick = Tick + build.Get(AbilityStatType.ComboWindowTicks).ToInt();
            // КУЛДАУН ОТ ПОСЛЕДНЕГО УДАРА: отыгравший серию ждёт столько же после неё, сколько оборвавший — после окна.
            if (last) _abilityReadyTick[slot] = Tick + AbilityCooldownTicks(build);
            if (stage < WreckStages - 1) _wreck.Phase = WreckPhase.Follow;
            else
            {
                _wreck.Phase = WreckPhase.Hold;
                _wreck.HoldEndTick = Tick + WreckHoldTicks;
            }
            WreckSetClock(Tick, Tick + WreckAfterStrikeTicks(stage));
        }

        /// <summary>«Четвёртый удар»: земля вокруг героя, Radius (+ тело), урон × 3, оглушение 1 с.</summary>
        private void WreckFourthHit(AbilityBuild build)
        {
            int damage = WreckMomentum(build, 3, build.Get(AbilityStatType.Damage).ToInt() * WreckFourthDamageFactor);
            FixVec2 hero = Entities.Position[PlayerId];
            _wreck.ImpactPoint = hero;
            _wreck.ImpactRadius = build.Get(AbilityStatType.Radius);
            int count = CollectArc(hero, _wreck.Direction, _wreck.ImpactRadius, WreckGroundArc, _arcScratch);
            for (int c = 0; c < count; c++)
            {
                int id = _arcScratch[c];
                ApplyAbilityDamage(PlayerId, id, WreckBigGame(build, id, damage), _wreck.Slot, DamageType.Physical);
                if (_wreck.Phase == WreckPhase.None) return;
                if (Entities.Alive[id]) StunByTalent(id, WreckFourthStunTicks);
            }
        }

        /// <summary>
        /// Геометрия удара оземь (чистая): доля урона по зарядам Девятого вала, вынос точки удара,
        /// радиус круга, полоса и докуда по оси дойдёт фронт. Точка удара не дальше первой
        /// преграды оси — голова якоря на цепи ложится перед камнем, а не в нём; круг,
        /// как и махи, стен не проверяет. Заряды (06.10 вечером) растят урон, ширину и длину
        /// полосы; точку и круг выпада — нет.
        /// </summary>
        private void WreckSlamGeometry(AbilityBuild build, PelagForm form, int charges, FixVec2 hero, FixVec2 dir,
            out int percent, out Fix64 reach, out Fix64 radius, out Fix64 length, out Fix64 halfWidth,
            out Fix64 wallEnd, out bool stopped)
        {
            bool breakwater = form == PelagForm.WreckBreakwater;
            charges = form == PelagForm.WreckNinthWave ? WreckClamp(charges, 0, WreckNinthMaxCharges) : 0;
            percent = WreckNinthPercent(charges);
            reach = WreckSlamReach;
            radius = WreckSlamRadius;
            length = breakwater ? WreckBreakwaterLength : build.Get(AbilityStatType.LaneLength);
            if (charges >= WreckNinthMaxCharges) length += WreckNinthLengthGain;
            halfWidth = breakwater ? WreckBreakwaterHalfWidth : build.Get(AbilityStatType.Width) * Fix64.Ratio(percent, 200);
            wallEnd = WreckWallEnd(hero, dir, length, breakwater ? WreckBreakwaterWallBackoff : Fix64.Zero, out stopped);
            if (stopped && reach > wallEnd) reach = wallEnd;
        }

        /// <summary>
        /// Снимок удара оземь с нажатия этапа до удара (вид ведёт голову якоря к точке, HUD
        /// замирает по Sim): точка, круг и доля урона — всегда; полоса и преграды — только
        /// пока нет живого фронта (lane — в тик удара: новый фронт сменяет старый).
        /// Возвращает вынос точки удара вдоль направления.
        /// </summary>
        private Fix64 WreckAimSlam(AbilityBuild build, bool lane)
        {
            FixVec2 hero = Entities.Position[PlayerId], dir = _wreck.Direction;
            WreckSlamGeometry(build, FormAt(_wreck.Slot), _wreck.NinthCharges, hero, dir, out int percent, out Fix64 reach,
                out Fix64 radius, out Fix64 length, out Fix64 halfWidth, out Fix64 wallEnd, out bool stopped);
            _wreck.DamagePercent = percent;
            _wreck.ImpactPoint = hero + dir * reach;
            _wreck.ImpactRadius = radius;
            if (!lane && _wreck.WaveTick >= 0) return reach;
            _wreck.LaneOrigin = hero;
            _wreck.LaneDir = dir;
            _wreck.LaneLength = length;
            _wreck.LaneHalfWidth = halfWidth;
            _wreck.WallEnd = wallEnd;
            _wreck.WallStopped = stopped;
            return reach;
        }

        /// <summary>
        /// Удар оземь (выпад): точка в WreckSlamReach перед героем (преграда — ближе), круг —
        /// 2 × Damage (Девятый вал — × доля зарядов) и оглушение StunTicks (босс не оглушается),
        /// потом фронт вала (у Волнореза — стена) с первого шага в этот же тик; у Призрачного
        /// якоря вала нет — якорь заряжен и упадёт в точку удара (Simulation.Wreck.Forms).
        /// </summary>
        private void WreckSlamHit(AbilityBuild build)
        {
            int slot = _wreck.Slot, serial = _wreck.Serial;
            PelagForm form = FormAt(slot);
            bool breakwater = form == PelagForm.WreckBreakwater, ninth = form == PelagForm.WreckNinthWave;
            bool ghost = form == PelagForm.WreckGhostAnchor;
            Fix64 reach = WreckAimSlam(build, true);
            int percent = _wreck.DamagePercent;
            Fix64 step = breakwater ? WreckBreakwaterStep : WreckWaveStep;
            int steps = ghost ? 0 : WreckWaveSteps(reach, _wreck.WallEnd, step);

            _events.Add(new SimEvent(SimEventType.WreckSlam, PlayerId, -1, steps, ninth && _wreck.NinthCharges >= WreckNinthMaxCharges,
                _wreck.ImpactPoint, DamageType.Physical, DamageOrigin.Ability, (int)form));

            // Круг удара: все, чьё тело касается круга (босс — по корпусу).
            System.Array.Clear(_wreckWaveHits, 0, _wreckWaveHits.Length);
            int damage = WreckMomentum(build, WreckStages - 1, build.Get(AbilityStatType.Damage).ToInt() * 2 * percent / 100);
            int stun = build.Get(AbilityStatType.StunTicks).ToInt();
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (!WreckEnemy(i)) continue;
                Fix64 r = _wreck.ImpactRadius + ThicketBodyFrom(i, _wreck.ImpactPoint);
                if (FixVec2.DistanceSq(Entities.Position[i], _wreck.ImpactPoint) > r * r) continue;
                _wreckWaveHits[i] = 1;
                ApplyAbilityDamage(PlayerId, i, WreckBigGame(build, i, damage), slot, DamageType.Physical);
                if (_wreck.Serial != serial || _wreck.Phase == WreckPhase.None) return;
                if (Entities.Alive[i] && stun > 0) StunByTalent(i, stun);
            }

            // Призрачный якорь: вала нет — якорь упадёт в точку удара через WreckGhostDelayTicks.
            if (ghost) { ArmWreckGhost(damage); return; }

            // Фронт: вал (база, Девятый вал) или стена Волнореза; урон — по каждому задетому один раз.
            _wreck.WaveTick = Tick;
            _wreck.WaveStart = reach;
            _wreck.WaveStep = step;
            _wreck.WaveTravelTicks = steps;
            _wreck.WaveForm = breakwater ? PelagForm.WreckBreakwater : ninth ? PelagForm.WreckNinthWave : PelagForm.None;
            _wreck.WaveDamage = WreckMomentum(build, WreckStages - 1, build.Get(AbilityStatType.Damage).ToInt() * percent / 100);
            _wreck.CarriedCount = 0;
            System.Array.Clear(_wreckCarried, 0, _wreckCarried.Length);
            UpdateWreckWave();
        }

        /// <summary>Шагов фронта от start до end: ⌈(end − start) / step⌉, не меньше нуля.</summary>
        public static int WreckWaveSteps(Fix64 start, Fix64 end, Fix64 step)
        {
            if (end <= start || step.Raw <= 0) return 0;
            Fix64 q = (end - start) / step;
            return -((-q).ToInt());
        }

        /// <summary>
        /// Докуда по оси от героя доходит вал: до первой непроходимой точки (проба
        /// WreckWallProbeStep, проверка как у ходьбы — CanTravel) минус backoff, не дальше length.
        /// </summary>
        private Fix64 WreckWallEnd(FixVec2 origin, FixVec2 dir, Fix64 length, Fix64 backoff, out bool stopped)
        {
            stopped = false;
            if (_layout == null && _campWalkMap == null) return length;
            FixVec2 previous = origin;
            Fix64 free = Fix64.Zero;
            int samples = WreckWaveSteps(Fix64.Zero, length, WreckWallProbeStep);
            for (int k = 1; k <= samples; k++)
            {
                Fix64 d = k == samples ? length : WreckWallProbeStep * k;
                FixVec2 point = origin + dir * d;
                if (!CanTravel(previous, point, Fix64.Zero))
                {
                    stopped = true;
                    Fix64 end = free - backoff;
                    return end.Raw < 0 ? Fix64.Zero : end;
                }
                free = d;
                previous = point;
            }
            return length;
        }

        /// <summary>
        /// Каждый тик: следующий шаг фронта (шаг 1 — в тик удара). Тиком после последнего
        /// шага фронта нет — вид успевает показать край; стена в этот тик обрушивается.
        /// </summary>
        private void UpdateWreckWave()
        {
            if (_wreck.WaveTick < 0) return;
            int step = Tick - _wreck.WaveTick + 1;
            if (step <= _wreck.WaveTravelTicks)
            {
                if (_wreck.WaveForm == PelagForm.WreckBreakwater) SweepWreckWall(step); else SweepWreckWave(step);
                return;
            }
            int serial = _wreck.Serial;
            if (_wreck.WaveForm == PelagForm.WreckBreakwater) WreckBreakwaterCrash();
            if (_wreck.Serial == serial) _wreck.WaveTick = -1;
        }

        /// <summary>Полоса фронта за шаг step: [start + (step − 1)·шаг, min(start + step·шаг, конец)].</summary>
        private void WreckBand(int step, out Fix64 inner, out Fix64 outer)
        {
            inner = _wreck.WaveStart + _wreck.WaveStep * (step - 1);
            outer = _wreck.WaveStart + _wreck.WaveStep * step;
            if (outer > _wreck.WallEnd) outer = _wreck.WallEnd;
            if (inner > outer) inner = outer;
        }

        /// <summary>Тело касается полосы [inner, outer] (босс — любым кругом корпуса).</summary>
        private bool WreckInBand(int id, Fix64 inner, Fix64 outer)
        {
            if (ThicketHullActive(id))
                return ThicketHullInLane(id, _wreck.LaneOrigin + _wreck.LaneDir * inner, _wreck.LaneDir, outer - inner, _wreck.LaneHalfWidth);
            return InsideLane(id, _wreck.LaneOrigin, _wreck.LaneDir, inner, outer, _wreck.LaneHalfWidth);
        }

        /// <summary>Вал: задетый фронтом впервые (не кругом удара) — WaveDamage, сбивание, лёгкого отбрасывает по полосе.</summary>
        private void SweepWreckWave(int step)
        {
            WreckBand(step, out Fix64 inner, out Fix64 outer);
            AbilityBuild build = WreckBuildForWave;
            int serial = _wreck.Serial;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (_wreckWaveHits[i] != 0 || !WreckEnemy(i) || !WreckInBand(i, inner, outer)) continue;
                _wreckWaveHits[i] = 2;
                int damage = build != null ? WreckBigGame(build, i, _wreck.WaveDamage) : _wreck.WaveDamage;
                ApplyAbilityDamage(PlayerId, i, damage, _wreck.Slot, DamageType.Physical);
                if (_wreck.Serial != serial || _wreck.WaveTick < 0) return;
                if (!Entities.Alive[i]) continue;
                StunByTalent(i, WreckWaveStunTicks);
                WreckShove(i, _wreck.LaneDir, WreckWaveKnockback, WreckWaveKnockbackTicks, ForcedMotionKind.Knockback);
            }
        }

        /// <summary>Сборка для «По крупным» вала: слот серии ещё Крушение (вал живёт и после серии).</summary>
        private AbilityBuild WreckBuildForWave => WreckBuild;

        /// <summary>Толкнуть лёгкого (правило AbordageLight) по направлению. Толчок не перебивает идущее движение.</summary>
        private void WreckShove(int id, FixVec2 direction, Fix64 distance, int ticks, ForcedMotionKind kind)
        {
            if (!Entities.Alive[id] || !AbordageLight(id)) return;
            if (kind == ForcedMotionKind.Shoved && ForcedMotion.IsActive(Entities, id)) return;
            ForcedMotion.Begin(Entities, id, Entities.Position[id] + direction * distance, ticks, kind);
        }
    }
}
