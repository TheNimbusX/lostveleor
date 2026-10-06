using System;

namespace Game.Sim
{
    /// <summary>
    /// Хозяин Чащи: ход, пробуждение, фазы, выбор и ход действий ядра (рёв,
    /// лапа, топот). Числа и точки расширения — Simulation.ForestBoss.cs.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---------- ход ----------

        /// <summary>
        /// Ход босса — всегда целиком. Зовётся из MoveEnemies ДО проверки
        /// оглушения и волока: ни то, ни другое его не держит. Спит — стоит
        /// укоренённым; в действии — стоит и смотрит туда, куда бьёт (в
        /// пробуждении и рёве вступления — доворачивается к герою, в рёве порога —
        /// только первые ThicketRoarTurnTicks, ≤ 25°); иначе
        /// разворачивается к герою не быстрее 2,5° за тик и идёт только вдоль
        /// взгляда до ThicketHoldDistance, не дальше поводка от Home (точка появления;
        /// на поляне босса — её центр). Герой в дальней полосе или кайтит (лапа не
        /// достаёт ThicketKiteTicks подряд) — ход ×1,4 (ThicketFarWalkPercent, 2,8 м/с).
        /// </summary>
        private void MoveThicketMaster(int id)
        {
            // Скорость прошлого тика — для разгона; сама обнуляется сразу: стоящий босс стоит.
            FixVec2 previous = Entities.Velocity[id];
            Entities.Velocity[id] = FixVec2.Zero;
            if (!Entities.Alive[PlayerId]) return;
            ref var m = ref ThicketMemory[id];
            // Корпус держит проход и у спящего, и под Часами (PushOutOfThicketHulls).
            if (!m.Awake || Tick < m.FrozenUntil) return;
            var a = ThicketMasters[id];
            if (a.Serial != 0)
            {
                bool handled = false;
                ThicketMoveExtra(id, ref handled);
                if (handled) return;
                // Пробуждение и рёв фигуры по взгляду не имеют: стоит и доворачивается
                // к герою, чтобы после вступления не стоять к нему спиной. Рёв порога
                // (66/50/33) — только первые ThicketRoarTurnTicks своего шага, дальше
                // стоит: клип рёва не вертится на ногах под кружащим героем (ревью
                // 02.10, вечер: «прокручивается на месте»).
                if (a.Action == ThicketMasterAction.Wake || a.Action == ThicketMasterAction.Roar)
                {
                    if (a.Action == ThicketMasterAction.Wake || (a.Tag & ThicketRoarIntroBit) != 0
                        || Tick - a.StageStartTick <= ThicketRoarTurnTicks)
                        Entities.Facing[id] = TurnToward(Entities.Facing[id], Entities.Position[PlayerId] - Entities.Position[id],
                            ThicketTurnCos, ThicketTurnSin);
                }
                // Серия лапы: корпус доворачивает к удару 3,5° за тик (баланс 02.10, ночь; ход —
                // 2,5°) — доходит до его сектора к контакту, без рывка.
                else if (a.Action == ThicketMasterAction.Paw && a.Direction.LengthSq.Raw != 0)
                    Entities.Facing[id] = TurnToward(Entities.Facing[id], a.Direction, ThicketPawTurnCos, ThicketPawTurnSin);
                else if (a.Direction.LengthSq.Raw != 0) Entities.Facing[id] = a.Direction;
                return;
            }

            FixVec2 from = Entities.Position[id];
            FixVec2 toPlayer = Entities.Position[PlayerId] - from;
            FixVec2 heading = SteerHeading(id, toPlayer);
            Entities.Facing[id] = TurnToward(Entities.Facing[id], heading, ThicketTurnCos, ThicketTurnSin);
            FixVec2 facing = Entities.Facing[id];
            Fix64 step = Entities.MoveStep[id];
            // Дальний или кайтящий герой (ревью 02.10, ночь, «дальники»): сокращает дистанцию быстрее.
            if (m.OutOfReachTicks >= ThicketKiteTicks || ThicketHeroBand(id) == ThicketBand.Far)
                step = step * ThicketFarWalkPercent / 100;
            Fix64 wanted = Fix64.Zero;
            // Стоит и у корпуса: герой вплотную к голове или лапам дальше 3,22 от центра — шаг его бы толкал.
            if (toPlayer.LengthSq > ThicketHoldDistance * ThicketHoldDistance && heading.LengthSq.Raw != 0
                && !ThicketHeroAgainstHull(id, ThicketHoldGap))
            {
                var share = (FixVec2.Dot(facing, heading.Normalized()) - ThicketWalkAlignFrom)
                    / (Fix64.One - ThicketWalkAlignFrom);
                if (share > Fix64.Zero) wanted = step * Fix64.Min(share, Fix64.One);
            }
            // Разгон — как у всех (AccelerationTicks), скаляром вдоль взгляда.
            var speed = Fix64.Max(Fix64.Zero, FixVec2.Dot(previous, facing));
            var change = step / AccelerationTicks;
            speed = change.Raw <= 0 ? wanted : speed + Fix64.Clamp(wanted - speed, -change, change);
            FixVec2 delta = facing * speed;
            // Поводок: наружу за 10 м от точки появления не шагает.
            FixVec2 next = from + delta;
            Fix64 leashSq = ThicketLeash * ThicketLeash;
            if (FixVec2.DistanceSq(next, m.Home) > leashSq && FixVec2.DistanceSq(next, m.Home) > FixVec2.DistanceSq(from, m.Home))
                delta = FixVec2.Zero;
            if (delta.LengthSq.Raw != 0)
            {
                Entities.Position[id] = EnemyStep(id, from, delta);
                Entities.Velocity[id] = Entities.Position[id] - from;
            }
        }

        // ---------- тик ----------

        /// <summary>
        /// Действия боссов. Зовётся в Step после UpdateSplitters — после всех
        /// кастов и ударов героя этого тика: снятое здесь оглушение и волок
        /// не доживают до хода следующего тика.
        /// </summary>
        private void UpdateThicketMasters()
        {
            ThicketZonesTick();
            if (_thicketMemory == null) return;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestThicketMaster) continue;
                if (!Entities.Alive[id]) { CancelThicketAction(id); CancelThicketHazard(id); CancelThicketSeeds(id); continue; }
                // Не оглушается и не двигается чужой волей.
                if (Statuses.StunUntilTick[id] != 0) Statuses.StunUntilTick[id] = 0;
                if (ForcedMotion.IsActive(Entities, id)) ForcedMotion.Clear(Entities, id);
                // Под землёй огонь гаснет: горение не доживает до выхода. Во вступлении — тоже.
                if ((ThicketShielded(id) || ThicketIntroShields(id)) && Statuses.IsBurning(id)) Statuses.ClearBurn(id);
                ref var m = ref ThicketMemory[id];
                if (!Entities.Alive[PlayerId]) { CancelThicketAction(id); CancelThicketHazard(id); CancelThicketSeeds(id); continue; }
                UpdateThicketPhase(id);
                // Под Часами стоит и окно топота: тики остановки «рядом» не копятся.
                if (Tick < m.FrozenUntil) continue;
                if (m.Awake) RecordThicketNear(id);
                if (!m.Awake) { TryWakeThicketMaster(id); continue; }
                // Фоновая опасность (наслоение) идёт сама — до действия этого тика.
                AdvanceThicketHazard(id);
                // Терновник (08.10) — тоже сам, до действия: кусты прорастают, выпускают шипы, шипы летят, кусты вянут.
                if (Entities.Alive[id]) AdvanceThicketBushes(id);
                if (!Entities.Alive[id]) continue;
                if (ThicketMasters[id].Serial != 0)
                {
                    AdvanceThicketAction(id);
                    if (ThicketMasters[id].Serial != 0) continue;
                }
                // Рёв на пороге — после доигранного действия и раньше отдыха.
                if (m.RoarsPending != 0) { StartThicketRoar(id); continue; }
                if (Tick < m.NextActionTick) continue;
                // Конец рёва вступления — сразу первая атака: лапа или нырок к герою.
                var choice = ThicketIntroOpenerDue(id) ? ThicketIntroOpener(id) : ChooseThicketAction(id);
                if (choice != ThicketMasterAction.None) StartThicketAction(id, choice);
            }
        }

        /// <summary>Фаза по здоровью и пороги рёва 66/50/33 — каждый по разу.</summary>
        private void UpdateThicketPhase(int id)
        {
            ref var m = ref ThicketMemory[id];
            long health = Entities.Health[id], max = Entities.MaxHealth[id];
            int phase = health * 100 > max * BossAddFirstPercent ? 1 : health * 100 > max * BossAddSecondPercent ? 2 : 3;
            if (m.Awake && phase > m.Phase) m.Phase = phase;
            int seen = m.RoarsDone | m.RoarsPending;
            if (health * 100 <= max * BossAddFirstPercent && (seen & ThicketRoar66Bit) == 0) m.RoarsPending |= ThicketRoar66Bit;
            if (health * 100 <= max * 50 && (seen & ThicketRoar50Bit) == 0) m.RoarsPending |= ThicketRoar50Bit;
            if (health * 100 <= max * BossAddSecondPercent && (seen & ThicketRoar33Bit) == 0) m.RoarsPending |= ThicketRoar33Bit;
        }

        /// <summary>
        /// Счёт тика (до действия босса): окно «рядом» для отладки (1 — герой ближе 4 м
        /// между центрами); «за спиной» — дальше 100° от взгляда и не дальше 5,2 м: +1,
        /// иначе с нуля; «прижался» — ближе 5,2 м подряд, что бы босс ни делал (баланс
        /// 02.10, ночь; было — только пока у босса нет действия); дальняя полоса подряд;
        /// «лапа не достаёт» подряд. Под землёй (нырок) «за спиной» и «прижался» — с нуля.
        /// </summary>
        private void RecordThicketNear(int id)
        {
            ref var m = ref ThicketMemory[id];
            Fix64 near = ThicketStompNearRange;
            FixVec2 toHero = Entities.Position[PlayerId] - Entities.Position[id];
            Fix64 distanceSq = toHero.LengthSq;
            bool bit = distanceSq <= near * near;
            m.NearHi = ((m.NearHi << 1) | (m.NearLo >> 63)) & ThicketNearHiMask;
            m.NearLo = (m.NearLo << 1) | (bit ? 1UL : 0UL);
            bool shielded = ThicketShielded(id);
            Fix64 rear = ThicketRearStompRange;
            bool behind = !shielded && distanceSq.Raw != 0 && distanceSq <= rear * rear
                && FixVec2.Dot(Entities.Facing[id].Normalized(), toHero.Normalized()) < ThicketRearCos;
            m.RearTicks = behind ? Math.Min(m.RearTicks + 1, ThicketStompWindowTicks) : 0;
            Fix64 stomp = ThicketStompRadius;
            bool hug = !shielded && distanceSq <= stomp * stomp;
            m.HugTicks = hug ? Math.Min(m.HugTicks + 1, ThicketStompHugTicks) : 0;
            // Дальняя полоса для нырка — с запасом 1 м: дальник, держащийся у её края (6–7 м), счёт не рвёт.
            bool far = ThicketHeroBand(id) == ThicketBand.Far
                || (m.FarTicks > 0 && ThicketHullGap(id, Entities.Position[PlayerId]) >= ThicketFarGap - ThicketFarKeepSlack);
            m.FarTicks = far ? Math.Min(m.FarTicks + 1, ThicketStompWindowTicks) : 0;
            m.OutOfReachTicks = shielded || !ThicketPawInReach(id) ? Math.Min(m.OutOfReachTicks + 1, ThicketStompWindowTicks) : 0;
        }

        /// <summary>
        /// Сон. На поляне — до вступления: герой ступил на её пол или ранил
        /// босса, через подлёт камеры (ThicketIntroWakeDue). На стенде без
        /// поляны — не раньше ThicketMinSleepTicks после появления и только
        /// когда герой ближе 9 м — или уже ранил босса издали. Пробуждение — поза
        /// «вырывает лапы» (Wake) и сразу за ней вступительный рёв.
        /// </summary>
        private void TryWakeThicketMaster(int id)
        {
            ref var m = ref ThicketMemory[id];
            if (m.Clearing != 0)
            {
                if (!ThicketIntroWakeDue(id)) return;
            }
            else
            {
                if (Tick - m.SpawnTick < ThicketMinSleepTicks) return;
                bool near = FixVec2.DistanceSq(Entities.Position[PlayerId], Entities.Position[id]) <= ThicketWakeRange * ThicketWakeRange;
                bool hurt = Entities.Health[id] < Entities.MaxHealth[id];
                if (!near && !hurt) return;
            }
            m.Awake = true;
            m.WakeTick = Tick;
            m.Phase = 1;
            UpdateThicketPhase(id);
            m.RoarsPending |= ThicketRoarIntroBit;
            if (!m.RngSeeded)
            {
                m.Rng = new Pcg32(_encounterSeed ^ unchecked((ulong)(uint)id * 0x9E3779B97F4A7C15UL), ThicketStream);
                m.RngSeeded = true;
            }
            Entities.Aggro[id] = true;
            int end = Tick + ThicketWakeTicks;
            ThicketMasters[id] = new ThicketMasterState
            {
                Serial = ++_thicketSerial, Action = ThicketMasterAction.Wake, StartTick = Tick, StageStartTick = Tick,
                ImpactTick = end, LastImpactTick = end, EndTick = end, Stages = 1, HitResolved = true,
                Origin = Entities.Position[id], Direction = Entities.Facing[id], Target = Entities.Position[id],
            };
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThicketWake, Entities.Position[id]));
        }

        // ---------- выбор ----------

        /// <summary>Добавить вариант взвешенного выбора (для ThicketAddCandidates этапов 2–3).</summary>
        private void AddThicketCandidate(ThicketMasterAction action, int weight)
        {
            if (weight <= 0) return;
            _thicketCandidates ??= new ThicketMasterAction[ThicketActionSlots];
            _thicketWeights ??= new int[ThicketActionSlots];
            if (_thicketCandidateCount >= _thicketCandidates.Length) return;
            _thicketCandidates[_thicketCandidateCount] = action;
            _thicketWeights[_thicketCandidateCount] = weight;
            _thicketCandidateCount++;
        }

        /// <summary>
        /// Что начать (ревью 02.10, ночь: «лапа — основа»): принудительное этапов 2–3
        /// (связка нырка, буря) → топот «за спиной» → топот «прижался» → правила этапов
        /// 2–3 (ливень фазы 3, нырок по дальнему герою и, с 03.10, «под героя» по сроку в фазе 1)
        /// → взвешенный выбор из готовых своим потоком: серия лапы (герой в досягаемости), рядом с
        /// ней топот фазы 1 (03.10, ThicketStompPickReady), нырок «под героя» фаз 2–3 и касты фаз 2–3.
        /// Ничего не готово — идёт к герою (в средней полосе подходит под лапу). Поток
        /// тратится только при двух вариантах и больше.
        /// </summary>
        private ThicketMasterAction ChooseThicketAction(int id)
        {
            // Наслоение: пока идёт фоновая опасность (круги прорастания и ливня,
            // облака пыльцы этого босса) — только серии лапы: ни топота, ни
            // нырка, ни каста, ни бури, ни связки. Поток не тратится. Лежащая
            // пыльца держит только героя под лапой (ThicketHazardBlocks).
            if (ThicketHazardBlocks(id))
                return ThicketPawReady(id) ? ThicketMasterAction.Paw : ThicketMasterAction.None;
            var choice = ThicketMasterAction.None;
            ThicketChooseForced(id, ref choice);
            if (choice == ThicketMasterAction.None && (ThicketRearStompHolds(id) || ThicketStompRuleHolds(id)))
                choice = ThicketMasterAction.Stomp;
            if (choice == ThicketMasterAction.None) ThicketChooseRule(id, ref choice);
            if (choice != ThicketMasterAction.None) return choice;

            _thicketCandidateCount = 0;
            if (ThicketPawReady(id))
            {
                AddThicketCandidate(ThicketMasterAction.Paw, ThicketPawWeight);
                // Топот в жребии рядом с лапой (03.10: «участить аое в ближнем бою, а то он только
                // лапой машет на фазе 1»): фаза 1 и только когда и лапа готова — топот вместо серии,
                // а не потому, что лапа не достаёт. Фазы 2–3 — касты в жребии, связка «топот → лапа».
                if (ThicketMemory[id].Phase < 2 && ThicketStompPickReady(id))
                    AddThicketCandidate(ThicketMasterAction.Stomp, ThicketStompPickWeight);
            }
            ThicketAddCandidates(id);
            if (_thicketCandidateCount == 0) return ThicketMasterAction.None;
            if (_thicketCandidateCount == 1) return _thicketCandidates[0];
            int total = 0;
            for (int k = 0; k < _thicketCandidateCount; k++) total += _thicketWeights[k];
            ref var m = ref ThicketMemory[id];
            int roll = m.Rng.NextInt(0, total);
            for (int k = 0; k < _thicketCandidateCount; k++)
            {
                roll -= _thicketWeights[k];
                if (roll < 0) return _thicketCandidates[k];
            }
            return _thicketCandidates[_thicketCandidateCount - 1];
        }

        /// <summary>
        /// Топот «прижался» (баланс 02.10, ночь; ревью 02.10, ночь — 45 тиков без действия и
        /// вне лапы): герой ThicketStompHugTicks (240, 8 с) тиков ПОДРЯД ближе 5,2 м, что бы
        /// босс ни делал (HugTicks; мимо пробежавшего не топчет) — в первый свободный тик
        /// топот вместо серии лапы; своя перезарядка 180 и 90 от любого топота. Лапа теперь
        /// достаёт и бок («подмышку»), так что «прижался» — это «давно стоишь вплотную»:
        /// ритм «лапа, лапа, лапа — топот» у того, кто не отходит.
        /// </summary>
        private bool ThicketStompRuleHolds(int id)
            => ThicketMemory[id].HugTicks >= ThicketStompHugTicks
                && Tick >= ThicketReady[id * ThicketActionSlots + (int)ThicketMasterAction.Stomp]
                && Tick >= ThicketReady[id * ThicketActionSlots + ThicketRearStompSlot]
                && Tick + ThicketStompWindupTicks >= ThicketMemory[id].QuietUntil;

        /// <summary>
        /// Топот «за спиной» (ревью 02.10, вечер): герой 20 тиков подряд дальше 100° от
        /// взгляда и ближе 5,2 м — топчет вместо разворота, со своей перезарядкой 90 от
        /// начала прошлого топота (любого); правило «прижался» и его перезарядку 180 не ждёт.
        /// </summary>
        private bool ThicketRearStompHolds(int id)
            => ThicketMemory[id].RearTicks >= ThicketRearStompTicks
                && Tick >= ThicketReady[id * ThicketActionSlots + ThicketRearStompSlot]
                && Tick + ThicketStompWindupTicks >= ThicketMemory[id].QuietUntil;

        /// <summary>
        /// Топот в жребии рядом с лапой (03.10): герой центром в круге топота (HugTicks &gt; 0), своя
        /// перезарядка (ThicketStompPickSlot, 135 от начала прошлого топота) и 90 от любого топота
        /// готовы, круг ляжет не раньше окна ответа и встанет сейчас (бюджет крупных меток, такт
        /// круга и кольца) — иначе в жребий не идёт и не держит босса.
        /// </summary>
        private bool ThicketStompPickReady(int id)
        {
            ref var m = ref ThicketMemory[id];
            if (m.HugTicks == 0) return false;
            if (Tick < ThicketReady[id * ThicketActionSlots + ThicketStompPickSlot]
                || Tick < ThicketReady[id * ThicketActionSlots + ThicketRearStompSlot]) return false;
            int impact = Tick + ThicketStompWindupTicks, ring = impact + ThicketStompRingDelayTicks;
            if (impact < m.QuietUntil) return false;
            return BigMarkAllowed(id, 1, impact) && HeroContactAllowed(id, ring, ring);
        }

        /// <summary>Серия лапы может начаться: герой в досягаемости, и первый удар ляжет не раньше окна ответа.</summary>
        private bool ThicketPawReady(int id)
            => ThicketPawInReach(id) && Tick + ThicketPawWindupOf(id) >= ThicketMemory[id].QuietUntil;

        /// <summary>
        /// Лапа достаёт: герой ближе ThicketPawStartRange между центрами — или вплотную
        /// к корпусу (у лап он стоит до 3,75 м от центра), но не дальше сектора лапы — и
        /// в ±40° от взгляда или центром в секторе первого удара, доворачивающего к нему за
        /// замах (ThicketPawOpening, ≤ 59,5°): так достаёт и стоящего сбоку у передней лапы,
        /// и «под мышкой» между лапой и бедром (67–80°, 1,7–1,9 м от центра; баланс 02.10,
        /// ночь — раньше лапа туда не доставала, а корпус, доворачивая, возил героя лапой из
        /// сектора: теперь передние лапы в замахе не держат, ThicketPawsLifted).
        /// </summary>
        private bool ThicketPawInReach(int id)
        {
            FixVec2 boss = Entities.Position[id], hero = Entities.Position[PlayerId];
            FixVec2 toHero = hero - boss;
            Fix64 distanceSq = toHero.LengthSq;
            if (distanceSq.Raw == 0 || distanceSq > ThicketPawRadius * ThicketPawRadius) return false;
            if (distanceSq > ThicketPawStartRange * ThicketPawStartRange && !ThicketHeroAgainstHull(id, ThicketPawHullSlack))
                return false;
            if (FixVec2.Dot(Entities.Facing[id].Normalized(), toHero.Normalized()) >= ThicketPawFrontCos) return true;
            return TelegraphContains(ThicketPawStrikeSector(boss, ThicketPawOpening(id), 0), hero, Fix64.Zero);
        }

        /// <summary>
        /// Направление первого удара серии (ревью 02.10, ночь; было «вдоль взгляда»): к герою,
        /// но не дальше поворота корпуса за свой замах (3,5°/тик: 17 × 3,5° = 59,5° во всех фазах,
        /// ThicketPawOpenCos; тяжёлый замах 30 — тоже 59,5°; баланс 02.10, ночь — было 42,5°) —
        /// корпус доходит до сектора ровно к контакту, без рывка (как доворот следующих ударов
        /// ≤ 35°). Замах не растёт: «вбок» дальше лапа бьёт кромкой сектора.
        /// </summary>
        private FixVec2 ThicketPawOpening(int id)
        {
            FixVec2 facing = Entities.Facing[id].Normalized();
            FixVec2 toHero = Entities.Position[PlayerId] - Entities.Position[id];
            if (facing.LengthSq.Raw == 0) return toHero.Normalized();
            int windup = ThicketPawWindupOf(id);
            if (windup == ThicketPawWindupTicks) return TurnToward(facing, toHero, ThicketPawOpenCos, ThicketPawOpenSin);
            Fix64 turn = ThicketPawTurnStep * Math.Min(windup, ThicketPawWindupTicks);
            return TurnToward(facing, toHero, Fix64.Cos(turn), Fix64.Sin(turn));
        }

        // ---------- начало ----------

        private bool StartThicketAction(int id, ThicketMasterAction action)
        {
            switch (action)
            {
                case ThicketMasterAction.Paw: return StartThicketPaw(id);
                case ThicketMasterAction.Stomp: return StartThicketStomp(id);
                case ThicketMasterAction.Roar: StartThicketRoar(id); return true;
            }
            bool started = false;
            ThicketStartExtra(id, action, ref started);
            return started;
        }

        /// <summary>
        /// Новое действие: номер, поза, метка шага (если есть), событие Started.
        /// Для этапов 2–3 — общий вход: метку шага открывают они сами.
        /// </summary>
        private ref ThicketMasterState BeginThicketAction(int id, ThicketMasterAction action, int impact, int lastImpact,
            int end, int stages, FixVec2 direction, FixVec2 target)
        {
            ref var a = ref ThicketMasters[id];
            a = new ThicketMasterState
            {
                Serial = ++_thicketSerial, Action = action, StartTick = Tick, StageStartTick = Tick,
                ImpactTick = impact, LastImpactTick = lastImpact, EndTick = end, Stages = stages,
                Origin = Entities.Position[id], Direction = direction, Target = target,
            };
            if (direction.LengthSq.Raw != 0) Entities.Facing[id] = direction;
            Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                ThicketActionKind(action), Entities.Position[id]));
            return ref a;
        }

        /// <summary>Открывает метку текущего шага и запоминает её номер.</summary>
        private void OpenThicketMark(int id, ref ThicketMasterState a, in EnemyTelegraph shape, TelegraphFlags flags)
        {
            int slot = OpenTelegraph(id, shape, a.ImpactTick, a.ImpactTick + TelegraphLingerTicks, flags);
            a.TelegraphSerial = TryGetTelegraph(slot, out var t) ? t.Serial : 0;
        }

        /// <summary>
        /// Серия лапы: первый удар — правая лапа, сектор 100° от правого плеча, к
        /// герою с доворотом до 59,5° (ThicketPawOpening, ревью 02.10, ночь: было «вдоль
        /// взгляда»), замах 17 (30 при уроне больше 60); корпус доворачивает к удару
        /// 3,5°/тик; каждый следующий — через промежуток своей фазы (10–12 / 10–11 / 10–11,
        /// броски потока в начале серии — в Tag, ThicketPawGapOf), доворот к герою не больше
        /// 35° (NextThicketPaw). Метка каждого
        /// удара — SharedView (ревью 02.10, вечер: «обозначить, чтоб она более
        /// видимая и читаемая в бою была»): общий красный сектор на земле от знака
        /// удара до контакта, ровно фигура попадания; в бюджет крупных меток лапа
        /// не идёт (ближний сектор). Ударов — по фазе (фаза 2 — 2 или 3 своим
        /// потоком). Серия встаёт, только если все её удары укладываются в такт с
        /// чужими и не ближе ThicketOwnContactSpacingTicks к ударам своей фоновой
        /// опасности; не влезает 3 — пробует 2.
        /// </summary>
        private bool StartThicketPaw(int id)
        {
            int impact = Tick + ThicketPawWindupOf(id);
            if (impact < ThicketMemory[id].QuietUntil) return false;
            // Промежутки серии — броски своего потока (баланс 02.10, ночь); такт и наслоение
            // проверяются уже с ними. Не встала серия — броски потрачены, в следующий тик новые.
            ThicketPawGapRange(id, out int gapMin, out int gapMax);
            ref var rng = ref ThicketMemory[id].Rng;
            int gap1 = rng.NextInt(gapMin, gapMax + 1), gap2 = rng.NextInt(gapMin, gapMax + 1);
            int fewest = ThicketPawSeriesMin(id), most = ThicketPawSeriesMax(id), strikes = 0;
            for (int n = most; n >= fewest && strikes == 0; n--)
                if (ThicketPawSeriesFits(id, impact, n, gap1, gap2)) strikes = n;
            if (strikes == 0) return false;
            // Фаза 2: 2 или 3 — бросок своего потока, только когда влезают оба.
            if (strikes > fewest) strikes = ThicketMemory[id].Rng.NextInt(fewest, strikes + 1);
            int last = ThicketPawStrikeTick(impact, strikes - 1, gap1, gap2);
            FixVec2 facing = Entities.Facing[id];
            FixVec2 direction = ThicketPawOpening(id);
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Paw, impact, last,
                last + ThicketPawStrikeTicks + ThicketPawRecoveryTicks, strikes,
                direction, ThicketPawStrikeMiddle(Entities.Position[id], direction, 0));
            a.Tag = gap1 | (gap2 << 8);
            // Корпус не прыгает к удару: доворачивается за замах 3,5°/тик (MoveThicketMaster); передние
            // лапы в замахе подняты и героя у бока из сектора не возят (ThicketPawsLifted).
            if (facing.LengthSq.Raw != 0) Entities.Facing[id] = facing;
            OpenThicketMark(id, ref a, ThicketPawStrikeSector(a.Origin, a.Direction, 0), TelegraphFlags.SharedView);
            return true;
        }

        /// <summary>Промежутки серии лапы по фазе: 10–12 / 10–11 / 10–11 тиков (включительно).</summary>
        private void ThicketPawGapRange(int id, out int min, out int max)
        {
            int phase = ThicketMasterPhase(id);
            if (phase >= 3) { min = ThicketPawGapPhase3Min; max = ThicketPawGapPhase3Max; }
            else if (phase == 2) { min = ThicketPawGapPhase2Min; max = ThicketPawGapPhase2Max; }
            else { min = ThicketPawGapPhase1Min; max = ThicketPawGapPhase1Max; }
        }

        /// <summary>Тик удара strike (с 0) серии, первый удар которой — в first, промежутки gap1, gap2.</summary>
        private static int ThicketPawStrikeTick(int first, int strike, int gap1, int gap2)
            => first + (strike >= 1 ? gap1 : 0) + (strike >= 2 ? gap2 : 0);

        private bool ThicketPawSeriesFits(int id, int impact, int strikes, int gap1, int gap2)
        {
            int last = ThicketPawStrikeTick(impact, strikes - 1, gap1, gap2);
            return HeroContactAllowed(id, impact, last) && !ThicketHazardClash(id, impact, strikes, gap1, gap2);
        }

        /// <summary>Ударов в серии по фазе: меньше и больше (фаза 2 — 2..3).</summary>
        public int ThicketPawSeriesMin(int id)
        {
            int phase = ThicketMasterPhase(id);
            return phase >= 3 ? ThicketPawSeriesPhase3 : phase == 2 ? ThicketPawSeriesPhase2Min : ThicketPawSeriesPhase1;
        }

        public int ThicketPawSeriesMax(int id)
        {
            int phase = ThicketMasterPhase(id);
            return phase >= 3 ? ThicketPawSeriesPhase3 : phase == 2 ? ThicketPawSeriesPhase2Max : ThicketPawSeriesPhase1;
        }

        /// <summary>
        /// Дыбом и топот: круг 5,2 м вокруг себя (удар через 42), потом кольцо
        /// 5,2–7,5 м (через 15 после первого, метка встаёт в тик первого) —
        /// крупная метка весом 1 до второго удара, окно ответа после него. Перезарядки
        /// (ревью 02.10, ночь): любой топот — 90 до следующего любого (место
        /// ThicketRearStompSlot); «прижался» и связка нырка — ещё своя 180 до следующего
        /// «прижался»; «за спиной» свою не ставит.
        /// </summary>
        private bool StartThicketStomp(int id)
        {
            int impact = Tick + ThicketStompWindupTicks;
            int ring = impact + ThicketStompRingDelayTicks;
            if (impact < ThicketMemory[id].QuietUntil) return false;
            if (!BigMarkAllowed(id, 1, impact) || !HeroContactAllowed(id, ring, ring)) return false;
            ref var m = ref ThicketMemory[id];
            bool rear = m.RearTicks >= ThicketRearStompTicks;
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Stomp, impact, ring, ring + ThicketStompStrikeTicks, 2,
                Entities.Facing[id], Entities.Position[id]);
            OpenThicketMark(id, ref a, ThicketStompCircle(a.Origin), TelegraphFlags.SharedView);
            // Подмога растягивает перезарядки топота, ярость (×0,85) их не режет: 3 с и 6 с — пол.
            if (!rear)
                ThicketReady[id * ThicketActionSlots + (int)ThicketMasterAction.Stomp] =
                    Tick + Math.Max(ThicketScaled(id, ThicketStompCooldownTicks), ThicketStompCooldownTicks);
            ThicketReady[id * ThicketActionSlots + ThicketRearStompSlot] =
                Tick + Math.Max(ThicketScaled(id, ThicketRearStompCooldownTicks), ThicketStompSpacingTicks);
            // Топот в жребии рядом с лапой (03.10) — не раньше 135 от начала любого топота.
            ThicketReady[id * ThicketActionSlots + ThicketStompPickSlot] =
                Tick + Math.Max(ThicketScaled(id, ThicketStompPickCooldownTicks), ThicketStompSpacingTicks);
            // Счёт начинается заново: следующий топот — за новые 240 тиков прижатым (или 20 за спиной).
            m.NearLo = 0; m.NearHi = 0;
            m.RearTicks = 0;
            m.HugTicks = 0;
            return true;
        }

        /// <summary>
        /// Рёв: вступление и пороги 66/50/33. Обязателен — бюджета и такта не
        /// ждёт. Несколько порогов разом — один рёв закрывает все (Tag).
        /// </summary>
        private void StartThicketRoar(int id)
        {
            ref var m = ref ThicketMemory[id];
            int thresholds = m.RoarsPending;
            m.RoarsDone |= thresholds;
            m.RoarsPending = 0;
            int impact = Tick + ThicketRoarWindupTicks;
            int end = impact + ThicketRoarRecoveryTicks;
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Roar, impact, impact, end, 1,
                Entities.Facing[id], Entities.Position[id]);
            a.Tag = thresholds;
            OpenThicketMark(id, ref a, ThicketRoarRing(a.Origin), TelegraphFlags.SharedView);
        }

        // ---------- ход действия ----------

        private void AdvanceThicketAction(int id)
        {
            var a = ThicketMasters[id];
            switch (a.Action)
            {
                case ThicketMasterAction.Wake:
                    if (Tick >= a.EndTick) FinishThicketAction(id, restTicks: 0);
                    return;
                case ThicketMasterAction.Paw:
                case ThicketMasterAction.Stomp:
                    // Удар шага; следующий шаг (лапа, кольцо) встаёт в этот же тик.
                    if (!a.HitResolved && Tick >= a.ImpactTick)
                    {
                        if (a.Action == ThicketMasterAction.Paw) ResolveThicketPaw(id);
                        else ResolveThicketStomp(id);
                    }
                    a = ThicketMasters[id];
                    // Кадр контакта последнего удара — и отдых по фазе; окно ответа — QuietUntil.
                    if (a.Serial != 0 && a.HitResolved && a.Stage + 1 >= a.Stages && Tick >= a.EndTick)
                        FinishThicketAction(id, ThicketRestTicks(id));
                    return;
                case ThicketMasterAction.Roar:
                    if (!a.HitResolved && Tick >= a.ImpactTick) ResolveThicketRoar(id);
                    a = ThicketMasters[id];
                    if (a.Serial != 0 && a.HitResolved && Tick >= a.EndTick)
                    {
                        int thresholds = a.Tag;
                        FinishThicketAction(id, 0);
                        ThicketRoarDone(id, thresholds);
                    }
                    return;
            }
            ThicketAdvanceExtra(id);
        }

        /// <summary>
        /// Следующий удар серии — в тик контакта прошлого: через промежуток этого удара
        /// (ThicketPawGapOf: 10–12 / 10–11 / 10–11 по фазе), доворот к герою не больше
        /// поворота корпуса за промежуток (3,5°/тик) и не больше 35°, новая метка-сектор
        /// от плеча другой лапы (SharedView), событие Started с номером удара (знак за
        /// промежуток до удара).
        /// </summary>
        private void NextThicketPaw(int id, ref ThicketMasterState a)
        {
            FixVec2 toHero = Entities.Position[PlayerId] - Entities.Position[id];
            int gap = ThicketPawGapOf(a, a.Stage);
            Fix64 turn = ThicketPawRetargetAngle(gap);
            a.Direction = TurnToward(a.Direction, toHero, Fix64.Cos(turn), Fix64.Sin(turn));
            a.Stage++;
            a.StageStartTick = Tick;
            a.ImpactTick = Tick + gap;
            a.HitResolved = false;
            a.Origin = Entities.Position[id];
            a.Target = ThicketPawStrikeMiddle(a.Origin, a.Direction, a.Stage);
            OpenThicketMark(id, ref a, ThicketPawStrikeSector(a.Origin, a.Direction, a.Stage), TelegraphFlags.SharedView);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThicketPaw, a.Origin, a.Stage));
        }

        /// <summary>
        /// Контакт удара серии — ровно по нарисованному сектору (или по заготовке,
        /// если пул меток был полон). Следующий удар встаёт в этот же тик —
        /// до урона: отражение может убить босса внутри ApplyAbilityDamage.
        /// </summary>
        private void ResolveThicketPaw(int id)
        {
            ref var a = ref ThicketMasters[id];
            a.HitResolved = true;
            int stage = a.Stage;
            EnemyTelegraph shape = ThicketPawStrikeSector(a.Origin, a.Direction, stage);
            int slot = FindTelegraph(a.TelegraphSerial);
            if (slot >= 0 && TryGetTelegraph(slot, out var drawn) && drawn.IsActive) shape = drawn;
            ResolveTelegraphSerial(a.TelegraphSerial);
            FixVec2 target = a.Target;
            bool hit = Entities.Alive[PlayerId]
                && TelegraphContains(in shape, Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
            NoteMeleeContactOnHero();
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketPaw, target, stage, hit));
            if (stage + 1 < a.Stages) NextThicketPaw(id, ref a);
            else ThicketMemory[id].QuietUntil = Tick + ThicketWindowOf(id);
            // Второй и третий удары серии — 35% лапы (ThicketPawFollowUpDamagePercent).
            if (hit) ApplyAbilityDamage(id, PlayerId, ThicketPawStrikeDamageOf(id, stage), -1, DamageType.Physical);
        }

        /// <summary>
        /// Топот, удар шага: Stage 0 — круг 5,2, Stage 1 — кольцо 5,2–7,5 (×0,75).
        /// Отброс на 2 м от центра — только если урон прошёл. Кольцо встаёт в тик
        /// первого удара (до урона); кого ранил круг, кольцо не бьёт; герой, чей центр
        /// внутри 5,2, кольцом цел (ThicketStompRingHits).
        /// </summary>
        private void ResolveThicketStomp(int id)
        {
            ref var a = ref ThicketMasters[id];
            a.HitResolved = true;
            int stage = a.Stage;
            FixVec2 center = a.Origin;
            bool live = ResolveTelegraphSerial(a.TelegraphSerial) || a.TelegraphSerial == 0;
            bool spared = stage > 0 && (a.Tag & ThicketStompRing1HitBit) != 0;
            FixVec2 hero = Entities.Position[PlayerId];
            bool touches = stage == 0
                ? TelegraphContains(ThicketStompCircle(center), hero, Entities.BodyRadius[PlayerId])
                : ThicketStompRingHits(center, hero, Entities.BodyRadius[PlayerId]);
            bool hit = live && !spared && Entities.Alive[PlayerId] && touches;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketStomp, center, stage, hit));
            if (stage + 1 < a.Stages)
            {
                a.Stage = stage + 1;
                a.StageStartTick = Tick;
                a.ImpactTick = a.LastImpactTick;
                a.HitResolved = false;
                OpenThicketMark(id, ref a, ThicketStompRing(center), TelegraphFlags.SharedView);
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                    EnemyActionKind.ThicketStomp, center, a.Stage));
            }
            else ThicketMemory[id].QuietUntil = Tick + ThicketWindowOf(id);
            if (!hit) return;
            int health = Entities.Health[PlayerId];
            ApplyAbilityDamage(id, PlayerId, stage == 0 ? ThicketStompDamageOf(id) : ThicketStompRingDamageOf(id), -1,
                DamageType.Physical);
            if (!Entities.Alive[PlayerId] || Entities.Health[PlayerId] >= health) return;
            if (stage == 0) ThicketMasters[id].Tag |= ThicketStompRing1HitBit;
            ThicketKnockback(center, Entities.Facing[id]);
        }

        /// <summary>Рёв: урона нет, отброс на 2 м от босса — всем в кольце (кроме неуязвимого).</summary>
        private void ResolveThicketRoar(int id)
        {
            ref var a = ref ThicketMasters[id];
            a.HitResolved = true;
            FixVec2 center = a.Origin;
            bool live = ResolveTelegraphSerial(a.TelegraphSerial) || a.TelegraphSerial == 0;
            bool hit = live && Entities.Alive[PlayerId]
                && TelegraphContains(ThicketRoarRing(center), Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketRoar, center, 0, hit));
            if (hit) ThicketKnockback(center, Entities.Facing[id]);
        }

        /// <summary>
        /// Отброс героя на ThicketKnockbackDistance прочь от center, по шагам до
        /// первого препятствия, как круг когтей Вендиго. Неуязвимого и уже
        /// летящего (кувырок, другой отброс) не трогает.
        /// </summary>
        private void ThicketKnockback(FixVec2 center, FixVec2 fallback)
        {
            if (PlayerImmune || ForcedMotion.IsActive(Entities, PlayerId)) return;
            var hero = Entities.Position[PlayerId];
            var away = hero - center;
            var direction = away.LengthSq.Raw > 0 ? away.Normalized() : fallback.Normalized();
            if (direction.LengthSq.Raw == 0) return;
            var radius = Entities.BodyRadius[PlayerId];
            var target = hero;
            var piece = ThicketKnockbackDistance / 20;
            for (int step = 0; step < 20; step++)
            {
                var next = target + direction * piece;
                if ((_layout != null || _campWalkMap != null) && !CanTravel(target, next, radius)) break;
                target = next;
            }
            if (!target.Equals(hero))
                ForcedMotion.Begin(Entities, PlayerId, target, ThicketKnockbackTicks, ForcedMotionKind.Knockback);
        }

        // ---------- конец, отдых, перезарядки ----------

        /// <summary>Действие доиграно: снять, поставить отдых (со множителями) и сказать этапам 2–3.</summary>
        private void FinishThicketAction(int id, int restTicks)
        {
            var finished = ThicketMasters[id].Action;
            ThicketMasters[id] = default;
            ref var m = ref ThicketMemory[id];
            m.NextActionTick = Tick + (restTicks > 0 ? ThicketScaled(id, restTicks) : 0);
            ThicketFinishedExtra(id, finished);
        }

        /// <summary>
        /// Снимает идущее действие: смерть босса или героя. Несработавший шаг —
        /// событие Cancelled и угасание своих меток. Перезарядки остаются.
        /// </summary>
        private void CancelThicketAction(int id)
        {
            if (_thicketMasters == null) return;
            var a = _thicketMasters[id];
            if (a.Serial == 0) return;
            if (a.Action != ThicketMasterAction.Wake && !a.HitResolved)
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionCancelled, id, PlayerId,
                    ThicketActionKind(a.Action), Entities.Position[id], a.Stage));
            ThicketCancelExtra(id);
            _thicketMasters[id] = default;
            CancelTelegraphsOf(id);
            if (_thicketMemory != null) _thicketMemory[id].NextActionTick = Tick;
        }

        /// <summary>
        /// Окно ответа после серии лапы и кольца топота: 30 тиков во всех фазах (проверка находок
        /// 03.10; баланс 02.10, ночь, сжимал его до 26 / 22 в фазах 2–3). Подмога и ярость его не меняют.
        /// </summary>
        public int ThicketWindowOf(int id) => ThicketWindowTicks;

        /// <summary>Отдых после действия по фазе (до множителей): 30 / 20 / 12.</summary>
        private int ThicketRestTicks(int id)
        {
            int phase = ThicketMemory[id].Phase;
            return phase >= 3 ? ThicketRestPhase3Ticks : phase == 2 ? ThicketRestPhase2Ticks : ThicketRestPhase1Ticks;
        }

        /// <summary>Перезарядка действия от этого тика, со множителями подмоги и половины здоровья.</summary>
        private void SetThicketCooldown(int id, ThicketMasterAction action, int ticks)
            => ThicketReady[id * ThicketActionSlots + (int)action] = Tick + ThicketScaled(id, ticks);

        /// <summary>×1,25, пока жива подмога; ×0,85 с половины здоровья. Замахи сюда не ходят никогда.</summary>
        private int ThicketScaled(int id, int ticks)
        {
            long scaled = ticks;
            if (ThicketAddsAlive(id)) scaled = scaled * ThicketAddsCooldownPercent / 100;
            if ((long)Entities.Health[id] * 100 <= (long)Entities.MaxHealth[id] * 50)
                scaled = scaled * ThicketEnragedCooldownPercent / 100;
            return (int)scaled;
        }

        /// <summary>Жив ли кто-то из врагов, кроме самого босса: подмога.</summary>
        private bool ThicketAddsAlive(int id)
        {
            for (int i = 1; i < Entities.Count; i++)
                if (i != id && Entities.Alive[i] && Entities.Side[i] != Faction.Wole) return true;
            return false;
        }
    }
}
