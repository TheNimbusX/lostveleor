using System;
using System.Collections.Generic;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Игрок «средней руки». Ввод — тот же InputFrame, что собирает вид:
    /// ЛКМ по врагу (Attack + AttackTarget), ПКМ по земле (MoveOrder),
    /// кнопки способностей с точкой прицела. Решения только по тому, что
    /// видно на экране: позиции, метки на земле, плоды, кулдауны, лавидий.
    ///
    /// Вынесен из ArenaBalanceBench (этап 0 плана «Мобы леса v2») без правок
    /// поведения: тот же ввод на тех же сидах. Им пользуются стенд баланса и
    /// замеры «ощущения» боя (IArenaProbe) — чинить бота значит сдвигать
    /// цифры обоих.
    /// </summary>
    public sealed class ArenaBot
    {
        /// <summary>
        /// Уходит из метки, которая упадёт не позже чем через столько тиков.
        /// 12 — реакция живого игрока (0,4 с); первый прогон шёл с 8, и бот
        /// уворачивался лучше человека. ARENA_BENCH_DODGE_TICKS — для сравнения.
        /// </summary>
        private static readonly int DodgeTicks = ArenaBalanceBench.EnvInt("ARENA_BENCH_DODGE_TICKS", 12);

        /// <summary>Ниже этой доли здоровья бот берёт родник, если он на экране.</summary>
        private const int SpringBelowPercent = 60;

        /// <summary>Метки ближе этого тоже учитываются, чтобы не отступить в соседнюю.</summary>
        private const int WatchTicks = 24;

        private static readonly Fix64 DodgeMargin = Fix64.Ratio(15, 100);
        private static readonly Fix64 DodgeStep = Fix64.Ratio(15, 100);
        private static readonly Fix64 DodgeMaxDistance = Fix64.Ratio(39, 10);
        private static readonly Fix64 DodgeOvershoot = Fix64.Ratio(3, 10);

        // Приказ идти ставится дальше края: у точки приказа герой тормозит
        // (PlayerTravelStep), и точка у самой кромки съедала бы разгон.
        private static readonly Fix64 DodgeClickBeyond = Fix64.Ratio(3, 2);
        private static readonly Fix64 DirectRange = Fix64.FromInt(3);

        /// <summary>Докуда герой подходит к цели сам: чуть ближе его AttackReach (1,875 м).</summary>
        private static readonly Fix64 StopDistance = Fix64.Ratio(3, 2);
        private static readonly Fix64 SteerStep = Fix64.Ratio(6, 5);
        private static readonly FixVec2[] Directions = BuildDirections(16);

        private readonly GameSession _session;
        private readonly List<EnemyTelegraph> _threats = new List<EnemyTelegraph>();
        private readonly List<int> _threatLeft = new List<int>();

        private int _target = -1;
        private bool _dodging;
        private FixVec2 _dodgePoint;
        private int _dodgeUntil = -1;
        private int _pathUntil = -1, _progressTick = -1;
        private FixVec2 _progressFrom;
        private int _reachTarget = -1, _reachUntil = -1;
        private bool _reachValue;

        // Граф клеток маршрута текущей арены: четыре соседа на клетку.
        private int[] _links, _distance, _queue;
        private int _graphDepth = -1, _graphRun = -1;

        public int Dodges { get; private set; }
        public int Dashes { get; private set; }

        /// <summary>Бот сейчас уходит из метки или стоит снаружи, пока она не упадёт.</summary>
        public bool Dodging => _dodging;

        /// <summary>Кого бот бьёт (индекс сущности) или -1.</summary>
        public int Target => _target;

        public ArenaBot(GameSession session) => _session = session;

        public void EnterArena()
        {
            _target = _reachTarget = -1;
            _dodging = false;
            _dodgeUntil = _pathUntil = _progressTick = _reachUntil = -1;
            Dodges = Dashes = 0;
        }

        public InputFrame Decide()
        {
            RiftRun run = _session.Run;
            switch (run.Phase)
            {
                case RunPhase.Clearing: return Fight(run);
                case RunPhase.SeekingExit: return WalkOut(run);
                case RunPhase.ChoosingReward: return Command(PickReward(run));
                case RunPhase.ReplacingAbility: return Command(RunCommand.SalvageAbility);
                // Первая ветка — «усиление» обычной сложности; «Сложно» бот не берёт.
                case RunPhase.ChoosingRoute: return Command(RunCommand.ChooseRoute1);
                default: return InputFrame.Empty;
            }
        }

        private static InputFrame Command(RunCommand command)
        {
            var input = InputFrame.Empty;
            input.Command = (byte)command;
            return input;
        }

        /// <summary>
        /// Родник, если здоровья меньше 60%; иначе способность, пока есть
        /// пустой слот; иначе усиление; иначе первое, что не родник.
        /// </summary>
        private static RunCommand PickReward(RiftRun run)
        {
            if (run.ChoosingArtifact) return RunCommand.ChooseReward1;
            EntityStore e = run.Sim.Entities;
            if ((long)e.Health[Simulation.PlayerId] * 100 < (long)e.MaxHealth[Simulation.PlayerId] * SpringBelowPercent)
                for (int i = 0; i < RiftRun.RewardChoices; i++)
                    if (run.GetOffer(i).Kind == RewardKind.Spring) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
            bool free = run.Loadout.FreeSlot() >= 0;
            for (int i = 0; i < RiftRun.RewardChoices; i++)
                if (free && run.GetOffer(i).Kind == RewardKind.Ability) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
            for (int i = 0; i < RiftRun.RewardChoices; i++)
                if (run.GetOffer(i).Kind == RewardKind.Talent) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
            for (int i = 0; i < RiftRun.RewardChoices; i++)
                if (run.GetOffer(i).Kind != RewardKind.Spring) return (RunCommand)((int)RunCommand.ChooseReward1 + i);
            return RunCommand.ChooseReward1;
        }

        private InputFrame Fight(RiftRun run)
        {
            Simulation sim = run.Sim;
            EntityStore e = sim.Entities;
            FixVec2 pos = e.Position[Simulation.PlayerId];
            CollectThreats(sim);
            // В корнях уйти нельзя ни шагом, ни кувырком (Simulation.HeroSlow):
            // живой игрок бьёт, пока держат, а уход начинает, когда отпустят.
            if (sim.HeroRooted) _dodging = false;
            else if (Dodge(run, pos, out InputFrame dodge)) return dodge;

            var input = InputFrame.Empty;
            int target = PickTarget(e, pos);
            if (target < 0) return input;
            FixVec2 at = e.Position[target];
            HoldWhirlwind(sim, pos, ref input);
            if (!Stuck(sim, pos, at) && Reachable(run, pos, target))
            {
                input.Flags = (byte)InputFlags.Attack;
                input.AttackTarget = target;
                input.Aim = at;
                TryCast(sim, pos, target, ref input);
            }
            else
            {
                // Дерево или берег между героем и целью: ПКМ по клеткам маршрута,
                // ЛКМ зажата — бьёт того, кто окажется перед носом.
                input.Flags = (byte)(InputFlags.MoveOrder | InputFlags.Attack);
                input.Aim = Waypoint(run, pos, at);
            }
            return input;
        }

        /// <summary>Добежать до способности с элиты (если есть куда положить), потом к выходу.</summary>
        private InputFrame WalkOut(RiftRun run)
        {
            FixVec2 pos = run.Sim.Entities.Position[Simulation.PlayerId];
            FixVec2 goal = run.Map.ExitPoint(0);
            if (run.Loadout.FreeSlot() >= 0)
            {
                Fix64 best = Fix64.MaxValue;
                for (int d = 0; d < run.DropCount; d++)
                {
                    RunDrop drop = run.GetDrop(d);
                    if (drop.Claimed || drop.Offer.Kind != RewardKind.Ability) continue;
                    Fix64 distance = FixVec2.DistanceSq(pos, drop.Position);
                    if (distance < best) { best = distance; goal = drop.Position; }
                }
            }
            var input = InputFrame.Empty;
            input.Flags = (byte)InputFlags.MoveOrder;
            input.Aim = run.Map.CanTravel(pos, goal, run.Sim.Entities.BodyRadius[Simulation.PlayerId])
                ? goal : Waypoint(run, pos, goal);
            return input;
        }

        /// <summary>Ближайший живой враг; прежнюю цель не бросает ради того, кто ближе на метр.</summary>
        private int PickTarget(EntityStore e, FixVec2 pos)
        {
            int best = -1;
            Fix64 bestDistance = Fix64.MaxValue;
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                Fix64 distance = FixVec2.DistanceSq(pos, e.Position[i]);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            if (_target > 0 && _target < e.Count && e.Alive[_target] && e.Side[_target] != Faction.Wole && best >= 0
                && FixVec2.Distance(pos, e.Position[_target]) <= Fix64.Sqrt(bestDistance) + Fix64.One)
                return _target;
            _target = best;
            return best;
        }

        // ---- метки на земле ----

        /// <summary>
        /// Всё, что нарисовано на земле и ещё не упало: общий список меток
        /// (сектор Хранителя и метки Вендиго/Камнекопыта — у тех свой вид, но
        /// игрок их тоже видит), точки падения плодов, путь шипа Шипомёта и
        /// взмах клыками Камнекопыта (метки нет, игрок видит замах на теле).
        /// </summary>
        private void CollectThreats(Simulation sim)
        {
            _threats.Clear();
            _threatLeft.Clear();
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
            {
                if (!sim.TryGetTelegraph(slot, out EnemyTelegraph t) || !t.IsActive) continue;
                AddThreat(t, t.ImpactTick - sim.Tick);
            }
            CollectThornShots(sim);
            CollectStonehoofTusks(sim);
            if (sim.ForestFruitActiveCount == 0) return;
            for (int slot = 0; slot < sim.ForestFruitCapacity; slot++)
                if (sim.TryGetForestFruit(slot, out ForestFruitState fruit))
                    AddThreat(EnemyTelegraph.Circle(fruit.Target, fruit.Radius), fruit.ImpactTick - sim.Tick);
        }

        /// <summary>
        /// Шип выстрела Шипомёта метки не рисует: живой игрок видит бросок
        /// (замах 21 тик, стрелок смотрит на него) и сам шип. Угроза — ещё не
        /// пройденная часть пути шириной в толщину шипа; срок — когда остриё
        /// дойдёт до героя. Та же реакция DodgeTicks, что и на метки.
        /// </summary>
        private void CollectThornShots(Simulation sim)
        {
            EntityStore e = sim.Entities;
            FixVec2 hero = e.Position[Simulation.PlayerId];
            Fix64 body = e.BodyRadius[Simulation.PlayerId];
            for (int id = 1; id < e.Count; id++)
            {
                if (e.Kind[id] != EnemyKind.ForestThorncaster) continue;
                ThornShotState shot;
                if (!sim.TryGetThornShot(id, out shot))
                {
                    // Ещё в замахе: путь уже известен — направление зафиксировано в его начале.
                    if (!sim.TryGetThorncasterAction(id, out ThorncasterState a) || a.Action != ThornAction.Shot
                        || a.Erupted > 0) continue;
                    shot = new ThornShotState
                    {
                        Serial = a.Serial, ReleaseTick = a.ImpactTick, Origin = a.Origin + a.Direction * Simulation.ThornShotStartOffset,
                        Direction = a.Direction, Length = sim.ThornShotFlightLength(a.Origin, a.Direction),
                    };
                }
                Fix64 along = FixVec2.Dot(hero - shot.Origin, shot.Direction) - body;
                int arrive = shot.ReleaseTick + Math.Max(0, ((along / Simulation.ThornShotSpeed).ToInt()));
                AddThreat(Simulation.ThornShotSweep(in shot, shot.Travelled, shot.Length), arrive - sim.Tick);
            }
        }

        /// <summary>
        /// Взмах клыками Камнекопыта на землю не ложится: игрок видит замах на
        /// теле кабана (14 тиков, направление зафиксировано в начале). Угроза —
        /// сектор клыков от тела до контакта; та же реакция DodgeTicks.
        /// </summary>
        private void CollectStonehoofTusks(Simulation sim)
        {
            EntityStore e = sim.Entities;
            for (int id = 1; id < e.Count; id++)
            {
                if (e.Kind[id] != EnemyKind.ForestStonehoof || !e.Alive[id]) continue;
                if (!sim.TryGetStonehoofTusk(id, out StonehoofTuskState tusk) || tusk.HitResolved) continue;
                AddThreat(Simulation.StonehoofTuskShape(e.Position[id], tusk.Direction), tusk.ImpactTick - sim.Tick);
            }
        }

        private void AddThreat(in EnemyTelegraph shape, int left)
        {
            if (left < 0 || left > WatchTicks) return;
            _threats.Add(shape);
            _threatLeft.Add(left);
        }

        private bool Unsafe(FixVec2 point, Fix64 body)
        {
            for (int k = 0; k < _threats.Count; k++)
                if (Simulation.TelegraphContains(_threats[k], point, body)) return true;
            return false;
        }

        /// <summary>
        /// Метка накроет героя через DodgeTicks или раньше — шаг наружу по
        /// кратчайшему пути. Снаружи герой стоит, пока метка не упадёт: иначе
        /// автоатака на следующем тике завела бы его обратно под удар.
        /// </summary>
        private bool Dodge(RiftRun run, FixVec2 pos, out InputFrame input)
        {
            input = InputFrame.Empty;
            Simulation sim = run.Sim;
            EntityStore e = sim.Entities;
            Fix64 body = e.BodyRadius[Simulation.PlayerId] + DodgeMargin;
            int soonest = int.MaxValue;
            for (int k = 0; k < _threats.Count; k++)
            {
                if (_threatLeft[k] > DodgeTicks || !Simulation.TelegraphContains(_threats[k], pos, body)) continue;
                soonest = Math.Min(soonest, _threatLeft[k]);
                _dodgeUntil = Math.Max(_dodgeUntil, sim.Tick + _threatLeft[k] + 1);
            }
            bool danger = soonest != int.MaxValue;
            if (!danger && sim.Tick > _dodgeUntil) { _dodging = false; return false; }
            if (!danger)
            {
                // Уже снаружи: приказ идти в точку выхода живёт в симуляции сам,
                // а зажатая атака бьёт того, кто стоит перед носом.
                input.Flags = (byte)InputFlags.Attack;
                int near = PickTarget(e, pos);
                input.Aim = near >= 0 ? e.Position[near] : pos + e.Facing[Simulation.PlayerId];
                return true;
            }

            if (!_dodging || Unsafe(_dodgePoint, body))
            {
                if (!_dodging) Dodges++;
                _dodging = true;
                int target = PickTarget(e, pos);
                _dodgePoint = Escape(run, pos, body, target >= 0 ? e.Position[target] : pos, soonest,
                    out FixVec2 direction, out bool onFoot);
                // Пешком не успеть — кувырок, если готов. Рывок общий у всех героев.
                AbilityBuild dash = sim.GetAbility(PelagKit.DashSlot);
                if (!onFoot && dash != null && sim.Tick >= sim.AbilityReadyTick(PelagKit.DashSlot))
                {
                    input.AbilityMask = (byte)(1 << PelagKit.DashSlot);
                    input.Flags = (byte)InputFlags.MoveOrder;
                    input.Aim = pos + direction * dash.Get(AbilityStatType.Radius);
                    _dodgePoint = input.Aim;
                    Dashes++;
                    return true;
                }
            }
            input.Flags = (byte)InputFlags.MoveOrder;
            input.Aim = _dodgePoint;
            return true;
        }

        /// <summary>
        /// Сколько герой пройдёт вдоль direction за ticks шагов с текущей
        /// скоростью: разгон и разворот ограничены так же, как в Approach
        /// (треть полной скорости за тик), начатый замах режет скорость на четверть.
        /// </summary>
        private static Fix64 ReachAlong(Simulation sim, FixVec2 direction, int ticks)
        {
            EntityStore e = sim.Entities;
            Fix64 full = e.MoveStep[Simulation.PlayerId];
            Fix64 cap = e.PendingAttackTarget[Simulation.PlayerId] > 0 || sim.PlayerAction.ActiveAt(sim.Tick)
                ? full * Fix64.Ratio(3, 4) : full;
            Fix64 change = full / 3, speed = FixVec2.Dot(e.Velocity[Simulation.PlayerId], direction), total = Fix64.Zero;
            // Ход идёт и в тик удара: движение в шаге раньше проверки попадания.
            for (int t = 0; t <= ticks; t++)
            {
                speed = Fix64.Min(speed + change, cap);
                total += speed;
            }
            return total;
        }

        /// <summary>
        /// Безопасная точка по шестнадцати направлениям. Сначала те, куда
        /// герой успевает дойти с учётом нынешней скорости; среди них — с
        /// кратчайшим путём, при равном — ближе к цели (после удара бот сразу
        /// наказывает). Не успевает никуда — направление с наименьшей
        /// нехваткой, и onFoot = false: пора кувыркаться.
        /// </summary>
        private FixVec2 Escape(RiftRun run, FixVec2 pos, Fix64 body, FixVec2 focus, int ticks,
            out FixVec2 direction, out bool onFoot)
        {
            Fix64 radius = run.Sim.Entities.BodyRadius[Simulation.PlayerId];
            int bestDir = -1;
            bool bestFeasible = false;
            Fix64 bestKey = Fix64.MaxValue, bestFocus = Fix64.MaxValue;
            FixVec2 bestPoint = pos;
            for (int k = 0; k < Directions.Length; k++)
            {
                for (Fix64 d = DodgeStep; d <= DodgeMaxDistance; d += DodgeStep)
                {
                    if (Unsafe(pos + Directions[k] * d, body)) continue;
                    Fix64 margin = ReachAlong(run.Sim, Directions[k], ticks) - d;
                    bool feasible = margin.Raw >= 0;
                    Fix64 key = feasible ? d : -margin;
                    FixVec2 point = pos + Directions[k] * (d + DodgeOvershoot);
                    Fix64 focus2 = FixVec2.DistanceSq(point, focus);
                    bool better = feasible != bestFeasible ? feasible
                        : key != bestKey ? key < bestKey : focus2 < bestFocus;
                    if (!better || !run.Map.IsWalkable(point, radius) || !run.Map.CanTravel(pos, point, radius)) break;
                    bestDir = k; bestFeasible = feasible; bestKey = key; bestFocus = focus2; bestPoint = point;
                    FixVec2 click = pos + Directions[k] * (d + DodgeClickBeyond);
                    if (run.Map.CanTravel(pos, click, radius)) bestPoint = click;
                    break;
                }
            }
            if (bestDir >= 0)
            {
                direction = Directions[bestDir];
                onFoot = bestFeasible;
                return bestPoint;
            }
            // Выхода нет (угол, стена) — прочь от начала ближайшей фигуры.
            FixVec2 away = pos - _threats[0].Origin;
            direction = away.LengthSq.Raw == 0 ? Directions[0] : away.Normalized();
            onFoot = false;
            return run.Map.ClampToWalkable(pos + direction * Fix64.FromInt(2), radius);
        }

        // ---- способности ----

        /// <summary>
        /// Одна кнопка за тик, по порядку слотов, когда готова, хватает лавидия
        /// и цель в её дальности. Стоя в метке, не кастует: длинный замах
        /// способности не даёт потом из неё выйти.
        /// </summary>
        private void TryCast(Simulation sim, FixVec2 pos, int target, ref InputFrame input)
        {
            EntityStore e = sim.Entities;
            if (Unsafe(pos, e.BodyRadius[Simulation.PlayerId] + DodgeMargin)) return;
            // Дальность — до края тела цели, как бьёт и сама способность.
            Fix64 reach = FixVec2.Distance(pos, e.Position[target]) - e.BodyRadius[target];
            for (int slot = 0; slot < PelagKit.MainSlots; slot++)
            {
                AbilityBuild build = sim.GetAbility(slot);
                // В корнях Абордаж и Шаг по цепи не начинаются: живой игрок
                // жмёт то, что бьёт с места, а не держит мёртвую кнопку.
                if (build == null || sim.AbilityHeldByRoots(slot)) continue;
                int id = build.DefinitionId;
                bool combo = id == AbilityDefinition.WreckId && sim.WreckComboOpen;
                if (!combo && (sim.Tick < sim.AbilityReadyTick(slot)
                    || e.Lavidium[Simulation.PlayerId] < Fix64.FromInt(Simulation.LavidiumCostOf(build)))) continue;
                Fix64 radius = build.Get(AbilityStatType.Radius);
                int abilityTarget = -1;
                bool use;
                if (id == AbilityDefinition.WhirlwindId) use = reach <= radius;
                else if (id == AbilityDefinition.BlazeId) use = reach <= Simulation.AutoAttackRange;
                else if (id == AbilityDefinition.AnchorLeapId) { use = reach <= radius && reach >= DirectRange; abilityTarget = target; }
                else if (id == AbilityDefinition.ChainStepId) { use = reach <= radius; abilityTarget = target; }
                else use = id != AbilityDefinition.DashId && reach <= radius;
                if (!use) continue;
                input.AbilityMask = (byte)(1 << slot);
                input.AbilityTarget = abilityTarget;
                return;
            }
        }

        /// <summary>Вихрь с талантом «канал» крутится, пока кнопка зажата, — держим, пока рядом враги.</summary>
        private static void HoldWhirlwind(Simulation sim, FixVec2 pos, ref InputFrame input)
        {
            EntityStore e = sim.Entities;
            for (int slot = 0; slot < PelagKit.MainSlots; slot++)
            {
                AbilityBuild build = sim.GetAbility(slot);
                if (build == null || build.DefinitionId != AbilityDefinition.WhirlwindId) continue;
                Fix64 radius = build.Get(AbilityStatType.Radius) + Fix64.One;
                for (int i = 1; i < e.Count; i++)
                    if (e.Alive[i] && e.Side[i] != Faction.Wole && FixVec2.DistanceSq(pos, e.Position[i]) <= radius * radius)
                    {
                        input.AbilityHoldMask |= (byte)(1 << slot);
                        return;
                    }
            }
        }

        // ---- дорога ----

        /// <summary>Полсекунды без продвижения вдали от цели — полторы секунды по клеткам маршрута.</summary>
        private bool Stuck(Simulation sim, FixVec2 pos, FixVec2 at)
        {
            if (_progressTick < 0 || sim.Tick - _progressTick >= 15)
            {
                Fix64 reach = Simulation.AutoAttackRange - Fix64.Ratio(1, 2);
                bool far = FixVec2.DistanceSq(pos, at) > reach * reach;
                if (_progressTick >= 0 && far && FixVec2.DistanceSq(pos, _progressFrom) < Fix64.Ratio(1, 25))
                    _pathUntil = sim.Tick + 45;
                _progressTick = sim.Tick;
                _progressFrom = pos;
            }
            return sim.Tick < _pathUntil;
        }

        /// <summary>
        /// Видна ли точка остановки у цели по прямой. Проверяется и вплотную:
        /// дерево между героем и роем в двух метрах держало обоих навсегда.
        /// Кэш на треть секунды.
        /// </summary>
        private bool Reachable(RiftRun run, FixVec2 pos, int target)
        {
            EntityStore e = run.Sim.Entities;
            FixVec2 at = e.Position[target];
            Fix64 distance = FixVec2.Distance(pos, at);
            if (distance <= StopDistance) return true;
            if (_reachTarget == target && run.Sim.Tick < _reachUntil) return _reachValue;
            FixVec2 stop = at - (at - pos) / distance * StopDistance;
            _reachValue = run.Map.CanTravel(pos, stop, e.BodyRadius[Simulation.PlayerId]);
            _reachTarget = target;
            _reachUntil = run.Sim.Tick + 10;
            return _reachValue;
        }

        /// <summary>
        /// Следующая точка пути по клеткам маршрута: волна от клетки цели,
        /// спуск по ней от клетки героя, и самая дальняя клетка, видная по прямой.
        /// </summary>
        private FixVec2 Waypoint(RiftRun run, FixVec2 from, FixVec2 goal)
        {
            Fix64 radius = run.Sim.Entities.BodyRadius[Simulation.PlayerId];
            FixVec2 point = RouteWaypoint(run, from, goal, radius);
            // Клетки маршрута не довели дальше своей: герой уже стоит в ней, и
            // приказ «иди в центр своей клетки» держал его у ствола до таймера.
            bool progress = FixVec2.DistanceSq(from, point) > Fix64.Ratio(9, 16);
            if (progress && run.Map.CanTravel(from, point, radius)) return point;
            FixVec2 toward = progress ? point : goal;
            return LocalWaypoint(run, from, toward, radius, out FixVec2 local) ? local : Steer(run, from, toward, radius);
        }

        // ---- обход вблизи ----
        //
        // Клетки маршрута — по два метра, и дерево между героем и мобом в
        // трёх метрах рвёт их граф: оба упирались в ствол с разных сторон
        // до конца таймера (стенд с реакцией 12 тиков насчитал 3–5 таких
        // «время вышло» на сорок забегов). Здесь волна по решётке в полметра
        // вокруг обоих, пересчёт раз в треть секунды.

        private const int LocalCells = 48;
        private static readonly Fix64 LocalStep = Fix64.Ratio(1, 2);
        private readonly int[] _localDistance = new int[LocalCells * LocalCells];
        private readonly int[] _localQueue = new int[LocalCells * LocalCells];
        private readonly bool[] _localOpen = new bool[LocalCells * LocalCells];
        private FixVec2 _localGoal, _localPoint;
        private int _localUntil = -1;
        private bool _localFound;

        private bool LocalWaypoint(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius, out FixVec2 waypoint)
        {
            int tick = run.Sim.Tick;
            if (tick < _localUntil && FixVec2.DistanceSq(goal, _localGoal) < Fix64.One
                && (!_localFound || run.Map.CanTravel(from, _localPoint, radius)))
            {
                waypoint = _localPoint;
                return _localFound;
            }
            _localUntil = tick + 10;
            _localGoal = goal;
            _localFound = SearchLocal(run, from, goal, radius, out _localPoint);
            waypoint = _localPoint;
            return _localFound;
        }

        private bool SearchLocal(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius, out FixVec2 waypoint)
        {
            waypoint = from;
            Fix64 half = LocalStep * (LocalCells / 2);
            FixVec2 origin = (from + goal) * Fix64.Ratio(1, 2) - new FixVec2(half, half);
            FixVec2 Center(int cell) => origin + new FixVec2(LocalStep * (cell % LocalCells) + LocalStep / 2,
                LocalStep * (cell / LocalCells) + LocalStep / 2);
            int CellOf(FixVec2 p)
            {
                int x = ((p.X - origin.X) / LocalStep).ToInt(), y = ((p.Y - origin.Y) / LocalStep).ToInt();
                return x < 0 || y < 0 || x >= LocalCells || y >= LocalCells ? -1 : y * LocalCells + x;
            }
            int start = CellOf(from), end = CellOf(goal);
            if (start < 0 || end < 0) return false;
            for (int i = 0; i < _localDistance.Length; i++)
            {
                _localDistance[i] = -1;
                _localOpen[i] = run.Map.IsWalkable(Center(i), radius);
            }
            // Цель стоит на месте моба — сама клетка может быть занята деревом рядом; ищем от неё.
            int head = 0, tail = 0;
            _localQueue[tail++] = end;
            _localDistance[end] = 0;
            while (head < tail && _localDistance[start] < 0)
            {
                int cell = _localQueue[head++];
                int cx = cell % LocalCells, cy = cell / LocalCells;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= LocalCells || ny >= LocalCells) continue;
                        int next = ny * LocalCells + nx;
                        if (_localDistance[next] >= 0) continue;
                        if (next != start && !_localOpen[next]) continue;
                        if (next != start && !run.Map.CanTravel(Center(cell), Center(next), radius)) continue;
                        _localDistance[next] = _localDistance[cell] + 1;
                        _localQueue[tail++] = next;
                    }
            }
            if (_localDistance[start] < 0) return false;
            // Спуск по волне от героя; берём самую дальнюю клетку пути, видную по прямой.
            bool found = false;
            for (int cell = start, guard = 0; cell != end && guard < _localDistance.Length; guard++)
            {
                int next = -1, cx = cell % LocalCells, cy = cell / LocalCells;
                for (int dy = -1; dy <= 1 && next < 0; dy++)
                    for (int dx = -1; dx <= 1 && next < 0; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= LocalCells || ny >= LocalCells) continue;
                        int candidate = ny * LocalCells + nx;
                        if (_localDistance[candidate] >= 0 && _localDistance[candidate] < _localDistance[cell]) next = candidate;
                    }
                if (next < 0) break;
                cell = next;
                FixVec2 center = Center(cell);
                if (!run.Map.CanTravel(from, center, radius)) break;
                waypoint = center;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Обход вблизи, внутри одной клетки маршрута: шаг на 1,2 м в ту из
        /// шестнадцати сторон, что проходима и ближе всего к цели.
        /// </summary>
        private static FixVec2 Steer(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius)
        {
            FixVec2 best = goal;
            Fix64 bestDistance = Fix64.MaxValue;
            for (int k = 0; k < Directions.Length; k++)
            {
                FixVec2 point = from + Directions[k] * SteerStep;
                if (!run.Map.CanTravel(from, point, radius)) continue;
                Fix64 distance = FixVec2.DistanceSq(point, goal);
                if (distance < bestDistance) { bestDistance = distance; best = point; }
            }
            return best;
        }

        private FixVec2 RouteWaypoint(RiftRun run, FixVec2 from, FixVec2 goal, Fix64 radius)
        {
            LayoutRoutes routes = run.Map.Routes;
            if (routes == null) return goal;
            EnsureGraph(run, radius);
            int start = NearestCell(routes, from), end = NearestCell(routes, goal);
            if (start < 0 || end < 0 || start == end) return goal;
            for (int i = 0; i < _distance.Length; i++) _distance[i] = -1;
            int head = 0, tail = 0;
            _queue[tail++] = end;
            _distance[end] = 0;
            while (head < tail && _distance[start] < 0)
            {
                int cell = _queue[head++];
                for (int d = 0; d < 4; d++)
                {
                    int next = _links[cell * 4 + d];
                    if (next < 0 || _distance[next] >= 0) continue;
                    _distance[next] = _distance[cell] + 1;
                    _queue[tail++] = next;
                }
            }
            // Своя клетка героя отрезана от графа (центр под деревом, отброс
            // тарана загнал к стволу): волна прошла все связные клетки, и путь
            // начинается с ближайшей из них, видной по прямой. Без этого бот
            // упирался в ствол до «время вышло» (проход 2 стенда: 7 из 13).
            if (_distance[start] < 0)
            {
                int entry = -1;
                Fix64 nearest = Fix64.MaxValue;
                for (int i = 0; i < routes.CellCount; i++)
                {
                    if (_distance[i] < 0) continue;
                    Fix64 d = FixVec2.DistanceSq(from, routes.GetCell(i).Center);
                    if (d < nearest && run.Map.CanTravel(from, routes.GetCell(i).Center, radius)) { nearest = d; entry = i; }
                }
                if (entry < 0) return goal;
                start = entry;
                if (start == end) return goal;
            }
            FixVec2 best = routes.GetCell(start).Center;
            for (int cell = start, guard = 0; cell != end && guard < _distance.Length; guard++)
            {
                int next = -1;
                for (int d = 0; d < 4 && next < 0; d++)
                {
                    int candidate = _links[cell * 4 + d];
                    if (candidate >= 0 && _distance[candidate] >= 0 && _distance[candidate] < _distance[cell]) next = candidate;
                }
                if (next < 0) break;
                cell = next;
                FixVec2 center = routes.GetCell(cell).Center;
                if (!run.Map.CanTravel(from, center, radius)) break;
                best = center;
                if (cell == end && run.Map.CanTravel(from, goal, radius)) return goal;
            }
            return best;
        }

        private void EnsureGraph(RiftRun run, Fix64 radius)
        {
            if (_graphDepth == run.Depth && _graphRun == _session.RunNumber && _links != null) return;
            _graphDepth = run.Depth;
            _graphRun = _session.RunNumber;
            LayoutRoutes routes = run.Map.Routes;
            int count = routes.CellCount;
            _links = new int[count * 4];
            _distance = new int[count];
            _queue = new int[count];
            Fix64 size = LayoutMap.CellSize;
            var steps = new[]
            {
                new FixVec2(size, Fix64.Zero), new FixVec2(-size, Fix64.Zero),
                new FixVec2(Fix64.Zero, size), new FixVec2(Fix64.Zero, -size),
            };
            for (int i = 0; i < count; i++)
            {
                FixVec2 center = routes.GetCell(i).Center;
                for (int d = 0; d < 4; d++)
                {
                    int next = routes.CellAt(center + steps[d]);
                    _links[i * 4 + d] = next >= 0 && run.Map.CanTravel(center, routes.GetCell(next).Center, radius) ? next : -1;
                }
            }
        }

        private static int NearestCell(LayoutRoutes routes, FixVec2 point)
        {
            int cell = routes.CellAt(point);
            if (cell >= 0) return cell;
            Fix64 best = Fix64.MaxValue;
            for (int i = 0; i < routes.CellCount; i++)
            {
                Fix64 distance = FixVec2.DistanceSq(point, routes.GetCell(i).Center);
                if (distance < best) { best = distance; cell = i; }
            }
            return cell;
        }

        private static FixVec2[] BuildDirections(int count)
        {
            var result = new FixVec2[count];
            for (int k = 0; k < count; k++) result[k] = FixVec2.FromAngle(Fix64.TwoPi * k / count);
            return result;
        }
    }
}
