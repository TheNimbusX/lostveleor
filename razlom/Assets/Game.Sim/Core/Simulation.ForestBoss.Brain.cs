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
        /// пробуждении и рёве — доворачивается к герою); иначе
        /// разворачивается к герою не быстрее 4,5° за тик и идёт только вдоль
        /// взгляда до ThicketHoldDistance, не дальше поводка от точки появления.
        /// </summary>
        private void MoveThicketMaster(int id)
        {
            // Скорость прошлого тика — для разгона; сама обнуляется сразу: стоящий босс стоит.
            FixVec2 previous = Entities.Velocity[id];
            Entities.Velocity[id] = FixVec2.Zero;
            if (!Entities.Alive[PlayerId]) return;
            ref var m = ref ThicketMemory[id];
            // Круп — часть тела: выталкивает героя и у спящего, и под Часами.
            if (!m.Awake || Tick < m.FrozenUntil) { PushHeroFromRump(id); return; }
            var a = ThicketMasters[id];
            if (a.Serial != 0)
            {
                bool handled = false;
                ThicketMoveExtra(id, ref handled);
                if (handled) return;
                // Пробуждение и рёв фигуры по взгляду не имеют: стоит и доворачивается
                // к герою, чтобы после вступления не стоять к нему спиной.
                if (a.Action == ThicketMasterAction.Wake || a.Action == ThicketMasterAction.Roar)
                    Entities.Facing[id] = TurnToward(Entities.Facing[id], Entities.Position[PlayerId] - Entities.Position[id],
                        ThicketTurnCos, ThicketTurnSin);
                // Серия лапы: следующий удар доворачивает не больше 9 шагов поворота —
                // корпус доходит до его сектора к контакту, без рывка.
                else if (a.Action == ThicketMasterAction.Paw && a.Direction.LengthSq.Raw != 0)
                    Entities.Facing[id] = TurnToward(Entities.Facing[id], a.Direction, ThicketTurnCos, ThicketTurnSin);
                else if (a.Direction.LengthSq.Raw != 0) Entities.Facing[id] = a.Direction;
                PushHeroFromRump(id);
                return;
            }

            FixVec2 from = Entities.Position[id];
            FixVec2 toPlayer = Entities.Position[PlayerId] - from;
            FixVec2 heading = SteerHeading(id, toPlayer);
            Entities.Facing[id] = TurnToward(Entities.Facing[id], heading, ThicketTurnCos, ThicketTurnSin);
            FixVec2 facing = Entities.Facing[id];
            Fix64 step = Entities.MoveStep[id];
            Fix64 wanted = Fix64.Zero;
            if (toPlayer.LengthSq > ThicketHoldDistance * ThicketHoldDistance && heading.LengthSq.Raw != 0)
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
            PushHeroFromRump(id);
        }

        /// <summary>
        /// Второй круг корпуса за телом: герой, вставший под круп, выталкивается
        /// наружу (не больше 0,3 м за тик, вдоль стен — как обычный шаг).
        /// Главное тело расталкивает общий SeparateBodies (вес босса 0).
        /// </summary>
        private void PushHeroFromRump(int id)
        {
            if (!Entities.Alive[PlayerId] || VoidPhased) return;
            // В нырке (от ухода до выхода) крупа нет — тело не держит проход.
            if (ThicketShielded(id)) return;
            FixVec2 center = ThicketRumpCenter(id);
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 offset = hero - center;
            Fix64 reach = ThicketRumpRadius + Entities.BodyRadius[PlayerId];
            Fix64 distanceSq = offset.LengthSq;
            if (distanceSq >= reach * reach) return;
            FixVec2 direction;
            Fix64 overlap;
            if (distanceSq.Raw == 0) { direction = -Entities.Facing[id].Normalized(); overlap = reach; }
            else { Fix64 distance = Fix64.Sqrt(distanceSq); direction = offset / distance; overlap = reach - distance; }
            if (direction.LengthSq.Raw == 0) return;
            Entities.Position[PlayerId] = MoveInsideLayout(PlayerId, hero, direction * Fix64.Min(overlap, ThicketRumpMaxPush));
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
                if (!Entities.Alive[id]) { CancelThicketAction(id); CancelThicketHazard(id); continue; }
                // Не оглушается и не двигается чужой волей.
                if (Statuses.StunUntilTick[id] != 0) Statuses.StunUntilTick[id] = 0;
                if (ForcedMotion.IsActive(Entities, id)) ForcedMotion.Clear(Entities, id);
                // Под землёй огонь гаснет: горение не доживает до выхода.
                if (ThicketShielded(id) && Statuses.IsBurning(id)) Statuses.ClearBurn(id);
                ref var m = ref ThicketMemory[id];
                if (!Entities.Alive[PlayerId]) { CancelThicketAction(id); CancelThicketHazard(id); continue; }
                UpdateThicketPhase(id);
                // Под Часами стоит и окно топота: тики остановки «рядом» не копятся.
                if (Tick < m.FrozenUntil) continue;
                if (m.Awake) RecordThicketNear(id);
                if (!m.Awake) { TryWakeThicketMaster(id); continue; }
                // Фоновая опасность (наслоение) идёт сама — до действия этого тика.
                AdvanceThicketHazard(id);
                if (!Entities.Alive[id]) continue;
                if (ThicketMasters[id].Serial != 0)
                {
                    AdvanceThicketAction(id);
                    if (ThicketMasters[id].Serial != 0) continue;
                }
                // Рёв на пороге — после доигранного действия и раньше отдыха.
                if (m.RoarsPending != 0) { StartThicketRoar(id); continue; }
                if (Tick < m.NextActionTick) continue;
                var choice = ChooseThicketAction(id);
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

        /// <summary>Окно топота сдвигается на тик: 1 — герой ближе 3,5 м между центрами.</summary>
        private void RecordThicketNear(int id)
        {
            ref var m = ref ThicketMemory[id];
            Fix64 near = ThicketStompNearRange;
            bool bit = FixVec2.DistanceSq(Entities.Position[PlayerId], Entities.Position[id]) <= near * near;
            m.NearHi = ((m.NearHi << 1) | (m.NearLo >> 63)) & ThicketNearHiMask;
            m.NearLo = (m.NearLo << 1) | (bit ? 1UL : 0UL);
        }

        /// <summary>
        /// Сон: не раньше ThicketMinSleepTicks после появления и только когда
        /// герой ближе 9 м — или уже ранил босса издали. Пробуждение — поза
        /// «вырывает лапы» (Wake) и сразу за ней вступительный рёв.
        /// </summary>
        private void TryWakeThicketMaster(int id)
        {
            ref var m = ref ThicketMemory[id];
            if (Tick - m.SpawnTick < ThicketMinSleepTicks) return;
            bool near = FixVec2.DistanceSq(Entities.Position[PlayerId], Entities.Position[id]) <= ThicketWakeRange * ThicketWakeRange;
            bool hurt = Entities.Health[id] < Entities.MaxHealth[id];
            if (!near && !hurt) return;
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
        /// Что начать: принудительное этапов 2–3 (буря, связки) → правило
        /// топота → правила этапа 2 (нырок) → взвешенный выбор из готовых
        /// своим потоком. Поток тратится только при двух вариантах и больше.
        /// </summary>
        private ThicketMasterAction ChooseThicketAction(int id)
        {
            // Наслоение: пока идёт фоновая опасность (круги прорастания и ливня,
            // облака пыльцы этого босса) — только серии лапы: ни топота, ни
            // нырка, ни каста, ни бури, ни связки. Поток не тратится.
            if (ThicketHazardActive(id))
                return ThicketPawReady(id) ? ThicketMasterAction.Paw : ThicketMasterAction.None;
            var choice = ThicketMasterAction.None;
            ThicketChooseForced(id, ref choice);
            if (choice == ThicketMasterAction.None && ThicketStompRuleHolds(id)) choice = ThicketMasterAction.Stomp;
            if (choice == ThicketMasterAction.None) ThicketChooseRule(id, ref choice);
            if (choice != ThicketMasterAction.None) return choice;

            _thicketCandidateCount = 0;
            if (ThicketPawReady(id)) AddThicketCandidate(ThicketMasterAction.Paw, 10);
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

        /// <summary>Топот: перезарядка готова и из последних 90 тиков герой был ближе 3,5 м не меньше 60.</summary>
        private bool ThicketStompRuleHolds(int id)
            => Tick >= ThicketReady[id * ThicketActionSlots + (int)ThicketMasterAction.Stomp]
                && ThicketMasterNearTicks(id) >= ThicketStompNearTicks
                && Tick + ThicketStompWindupTicks >= ThicketMemory[id].QuietUntil;

        /// <summary>Серия лапы может начаться: герой в досягаемости, и первый удар ляжет не раньше окна ответа.</summary>
        private bool ThicketPawReady(int id)
            => ThicketPawInReach(id) && Tick + ThicketPawWindupOf(id) >= ThicketMemory[id].QuietUntil;

        /// <summary>Лапа достаёт: герой ближе ThicketPawStartRange между центрами и в ±40° от взгляда.</summary>
        private bool ThicketPawInReach(int id)
        {
            FixVec2 toHero = Entities.Position[PlayerId] - Entities.Position[id];
            Fix64 distanceSq = toHero.LengthSq;
            if (distanceSq.Raw == 0 || distanceSq > ThicketPawStartRange * ThicketPawStartRange) return false;
            return FixVec2.Dot(Entities.Facing[id].Normalized(), toHero.Normalized()) >= ThicketPawFrontCos;
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
        /// Серия лапы: первый удар — сектор 120° на 4,14 м вдоль взгляда (к
        /// герою не доворачивает — заход сбоку имеет смысл), замах 15 (30 при
        /// уроне больше 60); каждый следующий — через 9 тиков, доворот к герою
        /// не больше 40,5° (NextThicketPaw). Метка — без SharedView: на земле
        /// её не рисуют, знак — уголёк на теле (событие Started со Stage);
        /// фигура в Sim — для попадания и бота. Ударов — по фазе (фаза 2 —
        /// 2 или 3 своим потоком). Серия встаёт, только если все её удары
        /// укладываются в такт с чужими и не ближе ThicketOwnContactSpacingTicks
        /// к ударам своей фоновой опасности; не влезает 3 — пробует 2.
        /// </summary>
        private bool StartThicketPaw(int id)
        {
            int impact = Tick + ThicketPawWindupOf(id);
            if (impact < ThicketMemory[id].QuietUntil) return false;
            int fewest = ThicketPawSeriesMin(id), most = ThicketPawSeriesMax(id), strikes = 0;
            for (int n = most; n >= fewest && strikes == 0; n--)
                if (ThicketPawSeriesFits(id, impact, n)) strikes = n;
            if (strikes == 0) return false;
            // Фаза 2: 2 или 3 — бросок своего потока, только когда влезают оба.
            if (strikes > fewest) strikes = ThicketMemory[id].Rng.NextInt(fewest, strikes + 1);
            int last = impact + ThicketPawSeriesGapTicks * (strikes - 1);
            FixVec2 direction = Entities.Facing[id].Normalized();
            if (direction.LengthSq.Raw == 0) direction = (Entities.Position[PlayerId] - Entities.Position[id]).Normalized();
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Paw, impact, last,
                last + ThicketPawStrikeTicks + ThicketPawRecoveryTicks, strikes,
                direction, Entities.Position[id] + direction * (ThicketPawRadius / 2));
            OpenThicketMark(id, ref a, ThicketPawSector(a.Origin, a.Direction), TelegraphFlags.None);
            return true;
        }

        private bool ThicketPawSeriesFits(int id, int impact, int strikes)
        {
            int last = impact + ThicketPawSeriesGapTicks * (strikes - 1);
            return HeroContactAllowed(id, impact, last) && !ThicketHazardClash(id, impact, strikes, ThicketPawSeriesGapTicks);
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
        /// Дыбом и топот: круг 5,2 м вокруг себя (удар через 24), потом кольцо
        /// 5,2–7,5 м (через 15 после первого, метка встаёт в тик первого) —
        /// крупная метка весом 1 до второго удара, окно 30 после него.
        /// </summary>
        private bool StartThicketStomp(int id)
        {
            int impact = Tick + ThicketStompWindupTicks;
            int ring = impact + ThicketStompRingDelayTicks;
            if (impact < ThicketMemory[id].QuietUntil) return false;
            if (!BigMarkAllowed(id, 1, impact) || !HeroContactAllowed(id, ring, ring)) return false;
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Stomp, impact, ring, ring + ThicketStompStrikeTicks, 2,
                Entities.Facing[id], Entities.Position[id]);
            OpenThicketMark(id, ref a, ThicketStompCircle(a.Origin), TelegraphFlags.SharedView);
            SetThicketCooldown(id, ThicketMasterAction.Stomp, ThicketStompCooldownTicks);
            // Окно начинается заново: следующий топот — за новые 60 тиков рядом.
            ref var m = ref ThicketMemory[id];
            m.NearLo = 0; m.NearHi = 0;
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
        /// Следующий удар серии — в тик контакта прошлого: доворот к герою не
        /// больше поворота за 9 тиков (40,5°), удар через 9, новая метка,
        /// событие Started с номером удара (знак на теле за 9 тиков до удара).
        /// </summary>
        private void NextThicketPaw(int id, ref ThicketMasterState a)
        {
            FixVec2 toHero = Entities.Position[PlayerId] - Entities.Position[id];
            a.Direction = TurnToward(a.Direction, toHero, ThicketPawRetargetCos, ThicketPawRetargetSin);
            a.Stage++;
            a.StageStartTick = Tick;
            a.ImpactTick = Tick + ThicketPawSeriesGapTicks;
            a.HitResolved = false;
            a.Origin = Entities.Position[id];
            a.Target = a.Origin + a.Direction * (ThicketPawRadius / 2);
            OpenThicketMark(id, ref a, ThicketPawSector(a.Origin, a.Direction), TelegraphFlags.None);
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
            EnemyTelegraph shape = ThicketPawSector(a.Origin, a.Direction);
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
            else ThicketMemory[id].QuietUntil = Tick + ThicketWindowTicks;
            if (hit) ApplyAbilityDamage(id, PlayerId, ThicketPawDamageOf(id), -1, DamageType.Physical);
        }

        /// <summary>
        /// Топот, удар шага: Stage 0 — круг 5,2, Stage 1 — кольцо 5,2–7,5 (×0,75).
        /// Отброс на 2 м от центра — только если урон прошёл. Кольцо встаёт в тик
        /// первого удара (до урона); кого ранил круг, кольцо не бьёт.
        /// </summary>
        private void ResolveThicketStomp(int id)
        {
            ref var a = ref ThicketMasters[id];
            a.HitResolved = true;
            int stage = a.Stage;
            FixVec2 center = a.Origin;
            bool live = ResolveTelegraphSerial(a.TelegraphSerial) || a.TelegraphSerial == 0;
            bool spared = stage > 0 && (a.Tag & ThicketStompRing1HitBit) != 0;
            EnemyTelegraph shape = stage == 0 ? ThicketStompCircle(center) : ThicketStompRing(center);
            bool hit = live && !spared && Entities.Alive[PlayerId]
                && TelegraphContains(in shape, Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
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
            else ThicketMemory[id].QuietUntil = Tick + ThicketWindowTicks;
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

        /// <summary>Отдых после действия по фазе (до множителей): 18 / 12 / 6.</summary>
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
