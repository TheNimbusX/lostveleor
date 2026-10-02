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
            // Под землёй (нырок) крупа нет — только бугор.
            if (ThicketUnderground(id)) return;
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
                if (!Entities.Alive[id]) { CancelThicketAction(id); continue; }
                // Не оглушается и не двигается чужой волей.
                if (Statuses.StunUntilTick[id] != 0) Statuses.StunUntilTick[id] = 0;
                if (ForcedMotion.IsActive(Entities, id)) ForcedMotion.Clear(Entities, id);
                ref var m = ref ThicketMemory[id];
                if (!Entities.Alive[PlayerId]) { CancelThicketAction(id); continue; }
                UpdateThicketPhase(id);
                // Под Часами стоит и окно топота: тики остановки «рядом» не копятся.
                if (Tick < m.FrozenUntil) continue;
                if (m.Awake) RecordThicketNear(id);
                if (!m.Awake) { TryWakeThicketMaster(id); continue; }
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
            var choice = ThicketMasterAction.None;
            ThicketChooseForced(id, ref choice);
            if (choice == ThicketMasterAction.None && ThicketStompRuleHolds(id)) choice = ThicketMasterAction.Stomp;
            if (choice == ThicketMasterAction.None) ThicketChooseRule(id, ref choice);
            if (choice != ThicketMasterAction.None) return choice;

            _thicketCandidateCount = 0;
            if (ThicketPawInReach(id)) AddThicketCandidate(ThicketMasterAction.Paw, 10);
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
                && ThicketMasterNearTicks(id) >= ThicketStompNearTicks;

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
        /// Лапа: сектор 120° на 3,6 м вдоль взгляда (к герою не доворачивает —
        /// заход сбоку имеет смысл). Метка — без SharedView: на земле её не
        /// рисуют, знак — уголёк на теле (событие Started); фигура в Sim — для
        /// попадания и бота. В фазе 3 — двойная: вторая лапа замахивается сразу
        /// после контакта первой, тем же замахом 24 тика.
        /// </summary>
        private bool StartThicketPaw(int id)
        {
            bool twin = ThicketMemory[id].Phase >= 3;
            int windup = ThicketPawWindupOf(id);
            int impact = Tick + windup;
            int last = twin ? impact + ThicketPawStrikeTicks + windup : impact;
            if (!HeroContactAllowed(id, impact, last)) return false;
            FixVec2 direction = Entities.Facing[id].Normalized();
            if (direction.LengthSq.Raw == 0) direction = (Entities.Position[PlayerId] - Entities.Position[id]).Normalized();
            int end = last + ThicketPawStrikeTicks + ThicketRecoveryOf(id, ThicketPawRecoveryTicks);
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Paw, impact, last, end, twin ? 2 : 1, direction,
                Entities.Position[id] + direction * (ThicketPawRadius / 2));
            OpenThicketMark(id, ref a, ThicketPawSector(a.Origin, a.Direction), TelegraphFlags.None);
            return true;
        }

        /// <summary>Дыбом и топот: круг 4,5 м вокруг себя, крупная метка весом 1.</summary>
        private bool StartThicketStomp(int id)
        {
            int impact = Tick + ThicketStompWindupTicks;
            if (!BigMarkAllowed(id, 1, impact)) return false;
            int end = impact + ThicketStompStrikeTicks + ThicketRecoveryOf(id, ThicketStompRecoveryTicks);
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Stomp, impact, impact, end, 1,
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
                    if (!a.HitResolved && Tick >= a.ImpactTick) ResolveThicketPaw(id);
                    a = ThicketMasters[id];
                    if (a.Serial == 0) return;
                    if (a.HitResolved && a.Stage + 1 < a.Stages && Tick >= a.ImpactTick + ThicketPawStrikeTicks)
                        NextThicketPaw(id);
                    else if (a.HitResolved && a.Stage + 1 >= a.Stages
                        && (Tick >= a.EndTick || ThicketChainCuts(id, a.LastImpactTick + ThicketPawStrikeTicks)))
                        FinishThicketAction(id, ThicketRestTicks(id));
                    return;
                case ThicketMasterAction.Stomp:
                case ThicketMasterAction.Roar:
                    if (!a.HitResolved && Tick >= a.ImpactTick)
                    {
                        if (a.Action == ThicketMasterAction.Stomp) ResolveThicketStomp(id);
                        else ResolveThicketRoar(id);
                    }
                    a = ThicketMasters[id];
                    if (a.Serial != 0 && a.HitResolved && Tick >= a.EndTick)
                    {
                        int thresholds = a.Tag;
                        bool roar = a.Action == ThicketMasterAction.Roar;
                        FinishThicketAction(id, roar ? 0 : ThicketRestTicks(id));
                        if (roar) ThicketRoarDone(id, thresholds);
                    }
                    return;
            }
            ThicketAdvanceExtra(id);
        }

        /// <summary>Вторая лапа двойной: доворот к герою не больше 30°, новый замах, новая метка.</summary>
        private void NextThicketPaw(int id)
        {
            ref var a = ref ThicketMasters[id];
            FixVec2 toHero = Entities.Position[PlayerId] - Entities.Position[id];
            a.Direction = TurnToward(a.Direction, toHero, ThicketPawRetargetCos, ThicketPawRetargetSin);
            a.Stage++;
            a.StageStartTick = Tick;
            a.ImpactTick = Tick + ThicketPawWindupOf(id);
            a.HitResolved = false;
            a.Origin = Entities.Position[id];
            a.Target = a.Origin + a.Direction * (ThicketPawRadius / 2);
            Entities.Facing[id] = a.Direction;
            OpenThicketMark(id, ref a, ThicketPawSector(a.Origin, a.Direction), TelegraphFlags.None);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThicketPaw, a.Origin, a.Stage));
        }

        /// <summary>Контакт лапы — ровно по нарисованному сектору (или по заготовке, если пул меток был полон).</summary>
        private void ResolveThicketPaw(int id)
        {
            ref var a = ref ThicketMasters[id];
            // Состояние — до урона: отражение может убить босса внутри ApplyAbilityDamage.
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
            if (hit) ApplyAbilityDamage(id, PlayerId, ThicketPawDamageOf(id), -1, DamageType.Physical);
        }

        /// <summary>Топот: урон по кругу и отброс на 2 м от центра — только если урон прошёл.</summary>
        private void ResolveThicketStomp(int id)
        {
            ref var a = ref ThicketMasters[id];
            a.HitResolved = true;
            FixVec2 center = a.Origin;
            bool live = ResolveTelegraphSerial(a.TelegraphSerial) || a.TelegraphSerial == 0;
            bool hit = live && Entities.Alive[PlayerId]
                && TelegraphContains(ThicketStompCircle(center), Entities.Position[PlayerId], Entities.BodyRadius[PlayerId]);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketStomp, center, 0, hit));
            if (!hit) return;
            int health = Entities.Health[PlayerId];
            ApplyAbilityDamage(id, PlayerId, ThicketStompDamageOf(id), -1, DamageType.Physical);
            if (Entities.Alive[PlayerId] && Entities.Health[PlayerId] < health)
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
        /// Связка фазы 3 (ThicketChainCutExtra) обрывает стойку после последнего
        /// контакта: следующий замах — сразу с тика from. Замах не трогается.
        /// </summary>
        private bool ThicketChainCuts(int id, int from)
        {
            if (Tick < from) return false;
            bool cut = false;
            ThicketChainCutExtra(id, ref cut);
            return cut;
        }

        /// <summary>
        /// Стойка после последнего контакта по фазе: 1 — как в таблице атак, 2 и
        /// 3 — ThicketPhase2/3RecoveryPercent. Замахов не касается.
        /// </summary>
        public int ThicketRecoveryOf(int id, int ticks)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster) return ticks;
            int phase = ThicketMemory[id].Phase;
            int percent = phase >= 3 ? ThicketPhase3RecoveryPercent : phase == 2 ? ThicketPhase2RecoveryPercent : 100;
            return ticks * percent / 100;
        }

        /// <summary>Отдых после действия по фазе (до множителей).</summary>
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
