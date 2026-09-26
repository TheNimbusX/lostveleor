using UnityEngine;
using Game.Sim;

namespace Game.View
{
    public sealed partial class TickDriver
    {
#if UNITY_EDITOR
        public static string StonehoofReviewCase;
        /// <summary>
        /// Сценарий героя против врага: запись Камнекопыта в редакторе или -capture-enemy-case
        /// изолированной съёмки. dodge — уходит из замаха, tank — стоит и принимает удары,
        /// stun — глушит якорем посреди замаха, turn — обходит врага по кругу, чтобы тот
        /// разворачивался на месте, death — добивает. Любой другой (wall) — стоит.
        /// </summary>
        public static string EnemyReviewCase => StonehoofReviewCase ?? CaptureRig.EnemyCase;
#else
        public static string EnemyReviewCase => CaptureRig.EnemyCase;
#endif
        private static readonly Fix64 EnemyTurnLead = Fix64.Ratio(7071, 10000);   // cos 45° = sin 45°
        private static readonly int AnchorSlamPool = FindPool(AbilityDefinition.AnchorSlamId);
        private int _enemyCaseGeneration = -1, _enemyCaseStart, _enemyCaseDodgeKey, _enemyCaseCastTick;
        private int _enemyCaseSeenImpact, _enemyCaseWindup, _enemyCaseSlamLead, _enemyCaseThreatSerial;
        private FixVec2 _enemyCaseAnchor, _enemyCaseDodgeTarget;

        // Общий уход из меток (dodge): запас к телу героя, как у бота стенда баланса; метка
        // ближе DodgeWatchTicks до контакта уже гонит героя наружу — 20 тиков хватает выйти
        // из круга корней (1,5 м) пешком.
        private const int DodgeWatchTicks = 20;
        private static readonly Fix64 DodgeMargin = Fix64.Ratio(15, 100), DodgeStep = Fix64.Ratio(1, 2),
            DodgeReach = Fix64.FromInt(5), DodgeOvershoot = Fix64.Ratio(3, 5);
        private static readonly FixVec2[] DodgeDirections = BuildDodgeDirections(16);

        private void CaptureStonehoofInput()
        {
            _pending = InputFrame.Empty; AttackHeld = false; _abilityLatch = _commandLatch = 0;
            _abilityPressLatched = _pointerPressLatched = false; _targetAimSlot = -1;
            // Только изолированный стенд: в обычном забеге съёмка ещё ждёт, пока стенд поднимется.
            if (Sim == null || Sim.Entities.Count < 2 || !Session.IsDeveloperRun) return;
            var entities = Sim.Entities; int tick = Sim.Tick;
            if (_enemyCaseGeneration != Generation)
            {
                _enemyCaseGeneration = Generation; _enemyCaseStart = tick;
                _enemyCaseAnchor = entities.Position[Simulation.PlayerId];
                _enemyCaseDodgeKey = _enemyCaseCastTick = _enemyCaseSeenImpact = _enemyCaseThreatSerial = -1;
                _enemyCaseWindup = 0; _enemyCaseSlamLead = 16;
            }
            int enemy = -1;
            for (int i = 1; i < entities.Count && enemy < 0; i++)
                if (entities.Alive[i] && entities.Side[i] != Faction.Wole) enemy = i;
            if (enemy < 0 || !entities.Alive[Simulation.PlayerId]) return;
            var hero = entities.Position[Simulation.PlayerId];
            switch (EnemyReviewCase)
            {
                case "dodge": CaptureEnemyDodge(tick, hero); break;
                case "stun": CaptureEnemyStun(enemy, tick, hero); break;
                case "turn": CaptureEnemyTurn(enemy, hero); break;
                case "death":
                    if (tick - _enemyCaseStart <= 100) break;
                    // Один HP: запись показывает смерть, а не долгую рубку.
                    if (entities.Health[enemy] > 1) entities.Health[enemy] = 1;
                    _pending.Flags = (byte)InputFlags.Attack; _pending.AttackTarget = enemy;
                    _pending.Aim = entities.Position[enemy]; AttackHeld = true;
                    break;
                // tank и wall: герой стоит, удары доходят как есть.
            }
        }

        private void CaptureEnemyDodge(int tick, FixVec2 hero)
        {
            var entities = Sim.Entities;
            for (int id = 1; id < entities.Count; id++)
            {
                if (!entities.Alive[id]) continue;
                if (Sim.TryGetStonehoofAction(id, out var charge))
                {
                    // Шаг вбок от линии разбега, пока бык не встал.
                    if (tick < charge.StartTick + 12 || tick >= charge.StopTick) continue;
                    var side = new FixVec2(-charge.Direction.Y, charge.Direction.X);
                    MoveCaptured(charge.Origin + charge.Direction * Fix64.FromInt(5) + side * Fix64.FromInt(4)); return;
                }
                if (Sim.TryGetWendigoAction(id, out var action))
                {
                    if (tick <= action.StartTick + 10 || tick >= action.ImpactTick) continue;
                    var side = new FixVec2(-action.Direction.Y, action.Direction.X);
                    MoveCaptured(action.Target + side * Fix64.FromInt(4)); return;
                }
            }
            // Всё остальное, что нарисовано на земле: сектор Хранителя и Расщепеня, линия
            // и всплеск Шипомёта, корни Корнехвата — и любая будущая метка без своей ветки.
            if (CaptureTelegraphDodge(tick, hero)) return;
            for (int id = 1; id < entities.Count; id++)
            {
                if (!entities.Alive[id]) continue;
                if (Sim.TryGetStonehoofAction(id, out _) || Sim.TryGetWendigoAction(id, out _)) continue;
                // Замах без метки на земле — укус корнеползов и детёнышей Расщепеня. Замах с
                // меткой ведёт общий уход выше: снаружи метки уходить от него незачем.
                if (Sim.TryGetEnemySwing(id, out var swing) && swing.Telegraph >= 0) continue;
                if (entities.PendingAttackTarget[id] != Simulation.PlayerId || tick >= entities.AttackImpactTick[id]) continue;
                // Замах ближнего боя: одна точка отскока на весь замах, вбок к исходной точке —
                // иначе герой за несколько уходов уползает с поляны.
                int key = entities.AttackImpactTick[id] * 64 + id;
                if (key != _enemyCaseDodgeKey)
                {
                    _enemyCaseDodgeKey = key;
                    var away = (hero - entities.Position[id]).Normalized();
                    var aside = new FixVec2(-away.Y, away.X);
                    if (FixVec2.Dot(_enemyCaseAnchor - hero, aside) < Fix64.Zero) aside = -aside;
                    _enemyCaseDodgeTarget = hero + away * Fix64.FromInt(2) + aside * Fix64.FromInt(2);
                }
                MoveCaptured(_enemyCaseDodgeTarget); return;
            }
        }

        /// <summary>
        /// Общий уход из меток, как у бота стенда баланса: метка из общего списка Sim, которая
        /// накроет героя не позже чем через DodgeWatchTicks, — шаг наружу, в ближайшую точку
        /// вне всех открытых меток; из равных — ближе к исходной точке, чтобы герой не уползал
        /// с поляны. Точка держится, пока её саму не накроет новая метка. Снаружи герой стоит:
        /// приказ идти живёт в Sim сам. Нарисованное и есть то, что бьёт, — поэтому хватает
        /// общего списка, без знания о каждом мобе.
        /// </summary>
        private bool CaptureTelegraphDodge(int tick, FixVec2 hero)
        {
            var sim = Sim;
            Fix64 body = sim.Entities.BodyRadius[Simulation.PlayerId] + DodgeMargin;
            int soonest = int.MaxValue, serial = -1;
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
            {
                if (!sim.TryGetTelegraph(slot, out var t) || !t.IsActive) continue;
                int left = t.ImpactTick - tick;
                if (left < 0 || left > DodgeWatchTicks || !Simulation.TelegraphContains(t, hero, body)) continue;
                if (left < soonest) { soonest = left; serial = t.Serial; }
            }
            if (serial < 0) return false;
            if (serial != _enemyCaseThreatSerial || UnderTelegraph(_enemyCaseDodgeTarget, body, tick))
            {
                _enemyCaseThreatSerial = serial;
                _enemyCaseDodgeTarget = TelegraphEscape(hero, body, tick);
            }
            MoveCaptured(_enemyCaseDodgeTarget);
            return true;
        }

        /// <summary>Накроет ли точку любая ещё открытая метка — и та, что упадёт позже.</summary>
        private bool UnderTelegraph(FixVec2 point, Fix64 body, int tick)
        {
            var sim = Sim;
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out var t) && t.IsActive && t.ImpactTick >= tick
                    && Simulation.TelegraphContains(t, point, body)) return true;
            return false;
        }

        /// <summary>
        /// Шестнадцать направлений, по каждому — первая безопасная точка через полметра.
        /// Счёт: вдвое путь наружу плюс удаление от исходной точки героя. Карта стенда
        /// (Run.Map) отсекает точки в стене и за ней; без карты — открытое поле.
        /// </summary>
        private FixVec2 TelegraphEscape(FixVec2 hero, Fix64 body, int tick)
        {
            LayoutMap map = Run != null ? Run.Map : null;
            Fix64 radius = Sim.Entities.BodyRadius[Simulation.PlayerId];
            FixVec2 best = hero; Fix64 bestScore = Fix64.MaxValue;
            for (int k = 0; k < DodgeDirections.Length; k++)
                for (Fix64 d = DodgeStep; d <= DodgeReach; d += DodgeStep)
                {
                    if (UnderTelegraph(hero + DodgeDirections[k] * d, body, tick)) continue;
                    FixVec2 goal = hero + DodgeDirections[k] * (d + DodgeOvershoot);
                    if (map != null && (!map.IsWalkable(goal, radius) || !map.CanTravel(hero, goal, radius))) break;
                    Fix64 score = d * 2 + FixVec2.Distance(goal, _enemyCaseAnchor);
                    if (score < bestScore) { bestScore = score; best = goal; }
                    break;
                }
            return best;
        }

        private static FixVec2[] BuildDodgeDirections(int count)
        {
            var result = new FixVec2[count];
            for (int k = 0; k < count; k++) result[k] = FixVec2.FromAngle(Fix64.TwoPi * Fix64.Ratio(k, count));
            return result;
        }

        /// <summary>Ближайшая открытая метка этого врага: начало и контакт замаха мобов без общего замаха.</summary>
        private bool TryEnemyTelegraph(int enemy, int tick, out int start, out int impact)
        {
            start = impact = -1;
            for (int slot = 0; slot < Sim.TelegraphHighWater; slot++)
            {
                if (!Sim.TryGetTelegraph(slot, out var t) || !t.IsActive || t.Source != enemy || t.ImpactTick <= tick) continue;
                if (impact < 0 || t.ImpactTick < impact) { start = t.StartTick; impact = t.ImpactTick; }
            }
            return impact >= 0;
        }

        /// <summary>
        /// Удар якорем (Оглушение) приходит посреди замаха: тик контакта якоря — середина замаха.
        /// Замах ближнего боя короче упреждения якоря, поэтому его начало предсказывается
        /// по NextAttackTick и длине, замеренной на первом увиденном замахе.
        /// </summary>
        private void CaptureEnemyStun(int enemy, int tick, FixVec2 hero)
        {
            var entities = Sim.Entities; var loadout = CurrentLoadout();
            if (AnchorSlamPool < 0) return;
            int slot = loadout.SlotOf(AnchorSlamPool);
            if (slot < 0) { loadout.Put(0, AnchorSlamPool); return; }
            if (_enemyCaseCastTick >= 0 && Sim.AnchorSlamActive && Sim.AnchorSlamImpactTick > _enemyCaseCastTick)
                _enemyCaseSlamLead = Sim.AnchorSlamImpactTick - _enemyCaseCastTick;

            var kind = entities.Kind[enemy];
            int start = -1, impact = -1;
            if (Sim.TryGetStonehoofAction(enemy, out var charge))
            { if (charge.Phase == StonehoofPhase.Windup) { start = charge.StartTick; impact = charge.LaunchTick; } }
            else if (Sim.TryGetWendigoAction(enemy, out var action))
            { if (tick < action.LaunchTick) { start = action.StartTick; impact = action.LaunchTick; } }
            else if (entities.PendingAttackTarget[enemy] >= 0 && tick < entities.AttackImpactTick[enemy])
            {
                if (entities.AttackImpactTick[enemy] != _enemyCaseSeenImpact)
                {
                    _enemyCaseSeenImpact = entities.AttackImpactTick[enemy];
                    _enemyCaseWindup = _enemyCaseSeenImpact - tick;
                }
                impact = _enemyCaseSeenImpact; start = impact - _enemyCaseWindup;
            }
            // Шипомёт и Корнехват общим замахом не бьют: начало и контакт — по их открытой метке.
            else if (TryEnemyTelegraph(enemy, tick, out int markStart, out int markImpact))
            { start = markStart; impact = markImpact; }
            else if (_enemyCaseWindup > 0 && entities.NextAttackTick[enemy] != int.MaxValue)
            {
                start = Mathf.Max(tick, Mathf.Max(entities.NextAttackTick[enemy], Sim.Statuses.StunUntilTick[enemy]));
                impact = start + _enemyCaseWindup;
            }

            // Держим дистанцию, с которой враг замахивается, а якорь достаёт: у быка 4–7 м,
            // у Шипомёта линия с 3 м (ближе — всплеск), у Корнехвата удар с 1,5 м.
            Fix64 hold = kind == EnemyKind.ForestStonehoof || kind == EnemyKind.ForestThorncaster
                || kind == EnemyKind.ForestRootSnarer ? Fix64.Ratio(43, 10)
                : kind == EnemyKind.ForestWendigo ? Fix64.Ratio(12, 5) : Fix64.Ratio(9, 5);
            var toEnemy = entities.Position[enemy] - hero;
            bool recent = _enemyCaseCastTick >= 0 && tick != _enemyCaseCastTick && tick - _enemyCaseCastTick < 30;
            bool ready = !recent && !Sim.AnchorSlamActive && Sim.AbilityReadyTick(slot) <= tick;
            bool timed = start >= 0 && tick + _enemyCaseSlamLead >= (start + impact) / 2 && tick + _enemyCaseSlamLead < impact;
            if (ready && timed && toEnemy.LengthSq <= (hold + Fix64.Ratio(1, 2)) * (hold + Fix64.Ratio(1, 2)))
            {
                // Лавидий — не предмет проверки: запись не должна встать из-за пустого запаса.
                entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(entities.MaxLavidium[Simulation.PlayerId]);
                _enemyCaseCastTick = tick; _abilityLatch = (byte)(1 << slot);
                _pending.Aim = entities.Position[enemy];
            }
            else if (toEnemy.LengthSq > (hold + Fix64.Ratio(3, 10)) * (hold + Fix64.Ratio(3, 10)))
                MoveCaptured(entities.Position[enemy] - toEnemy.Normalized() * hold);
        }

        /// <summary>
        /// Обход по кругу внутри дистанции врага: он стоит и доворачивается, а не идёт следом.
        /// Точка на 45° вперёд по кругу держит героя у окружности; обход против часовой.
        /// </summary>
        private void CaptureEnemyTurn(int enemy, FixVec2 hero)
        {
            var entities = Sim.Entities; var center = entities.Position[enemy]; var kind = entities.Kind[enemy];
            // Шипомёта обходим за чертой всплеска (2,6 м): иначе вместо разворота — удар вокруг себя.
            Fix64 radius = kind == EnemyKind.ForestStonehoof ? Fix64.Ratio(11, 2)
                : kind == EnemyKind.ForestThorncaster ? Fix64.Ratio(7, 2)
                : kind == EnemyKind.ForestRootSnarer ? Fix64.FromInt(3)
                : kind == EnemyKind.ForestWendigo ? Fix64.Ratio(11, 5) : Fix64.Ratio(8, 5);
            var from = hero - center;
            from = from.LengthSq < Fix64.Ratio(1, 100) ? new FixVec2(Fix64.One, Fix64.Zero) : from.Normalized();
            var ahead = new FixVec2(from.X * EnemyTurnLead - from.Y * EnemyTurnLead, from.X * EnemyTurnLead + from.Y * EnemyTurnLead);
            MoveCaptured(center + ahead * radius);
        }

        private void MoveCaptured(FixVec2 point)
        {
            _pending.Flags = (byte)InputFlags.MoveOrder; _pending.Aim = point;
        }

        private static int FindPool(int definitionId)
        {
            for (int i = 0; i < PelagKit.PoolSize; i++)
                if (PelagKit.PoolDefinition(i)?.Id == definitionId) return i;
            return -1;
        }

        public void StartStonehoofTest(LocationTheme theme, ulong seed, int count = 1, bool obstacle = false)
        {
            if (theme == null || theme.Gameplay == null) throw new System.ArgumentException("Нужен профиль леса.");
            theme.Style.Validate(); GetComponent<ArenaView>().PrepareStonehoof();
            Session.StartStonehoofTest(theme.Gameplay.ToDefinition(), seed, count, obstacle);
            var layout = GetComponent<LayoutView>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_developerThemeActive) _normalTheme = layout.Profile;
            _developerThemeActive = true;
#endif
            layout.Configure(theme); ClearCapturedInput(); SyncGeneration();
            Debug.Log("[stonehoof-test] Камнекопыт: 180 HP, 30 урона, подготовка 1 с, разбег 12 м/с. Прогресс не сохраняется.");
        }
    }
}
