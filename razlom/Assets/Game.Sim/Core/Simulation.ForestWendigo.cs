using System;
using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>Атака Вендиго. Значение идёт в хеш и в ActionVariant событий — новые только в конец.</summary>
    public enum WendigoAction : byte { None, Claw, Leap, Howl }

    public readonly struct WendigoActionState
    {
        public readonly int Serial, StartTick, LaunchTick, ImpactTick, EndTick;
        public readonly WendigoAction Kind;
        public readonly FixVec2 Origin, Target, Direction;
        public readonly bool HitResolved;
        public WendigoActionState(int serial, WendigoAction kind, int start, FixVec2 origin,
            FixVec2 target, FixVec2 direction, bool hitResolved = false)
        {
            Serial = serial; Kind = kind; StartTick = start; Origin = origin; Target = target;
            Direction = direction; HitResolved = hitResolved;
            // У когтя и воя взлёта нет: LaunchTick совпадает с контактом.
            ImpactTick = start + (kind == WendigoAction.Leap ? Simulation.WendigoLeapImpactTicks
                : kind == WendigoAction.Howl ? Simulation.WendigoHowlWindupTicks : Simulation.WendigoClawWindupTicks);
            LaunchTick = kind == WendigoAction.Leap ? start + Simulation.WendigoLeapLaunchTicks : ImpactTick;
            EndTick = ImpactTick + (kind == WendigoAction.Leap ? Simulation.WendigoLeapRecoveryTicks
                : kind == WendigoAction.Howl ? Simulation.WendigoHowlRecoveryTicks : Simulation.WendigoClawRecoveryTicks);
        }
        public WendigoActionState Resolve() => new WendigoActionState(Serial, Kind, StartTick, Origin, Target, Direction, true);
    }

    public sealed partial class Simulation
    {
        // Окна атак. Коготь: 18 тиков замаха, 12 стойки, 15 свободного хода —
        // цикл 45. Прыжок: взлёт на 24-м тике, приземление на 33-м, 18 стойки.
        public const int WendigoClawWindupTicks = 18, WendigoClawRecoveryTicks = 12;
        public const int WendigoClawRestTicks = 15;
        public const int WendigoClawCycleTicks = WendigoClawWindupTicks + WendigoClawRecoveryTicks + WendigoClawRestTicks;
        public const int WendigoLeapLaunchTicks = 24, WendigoLeapImpactTicks = 33, WendigoLeapRecoveryTicks = 18;
        public const int WendigoLeapCooldownTicks = 195;

        // «Вой чащи» (решение владельца от 26.09). Встаёт, 30 тиков набирает
        // воздух — кольцо на земле заполняется, — на 30-м бьёт всё кольцо и
        // замедляет героя, потом 24 тика стоит открытым. Перезарядка 270 тиков
        // от начала воя. Вой — крупная атака, как прыжок: без жетона не звучит.
        public const int WendigoHowlWindupTicks = 30, WendigoHowlRecoveryTicks = 24;
        public const int WendigoHowlCooldownTicks = 270;

        /// <summary>Замедление воя: минус 30% скорости бега на 30 тиков (1 с).</summary>
        public const int WendigoHowlSlowPercent = 30, WendigoHowlSlowTicks = 30;

        public static readonly Fix64 WendigoBodyRadius = EnemyArchetypes.WendigoBodyRadius;
        public static readonly Fix64 WendigoClawRange = Fix64.Ratio(27, 10);
        public static readonly Fix64 WendigoLeapRadius = Fix64.Ratio(125, 100);

        /// <summary>
        /// Кольцо воя: от 2 до 5,5 м вокруг Вендиго. Дыра у ног — место, где
        /// от воя спасаются, шагнув К зверю; снаружи — отбежав. Кольцо рисует
        /// общий GroundTelegraphView, и попадание считается по нему же.
        /// </summary>
        public static readonly Fix64 WendigoHowlInnerRadius = Fix64.FromInt(2);
        public static readonly Fix64 WendigoHowlOuterRadius = Fix64.Ratio(11, 2);

        /// <summary>Вой начинается, когда герой в 2–6 м (между центрами), а прыжок перезаряжается.</summary>
        public static readonly Fix64 WendigoHowlMinDistance = Fix64.FromInt(2);
        public static readonly Fix64 WendigoHowlMaxDistance = Fix64.FromInt(6);

        /// <summary>
        /// Походка: шаг только вдоль взгляда, и скорость растёт с тем, насколько
        /// взгляд совпал с направлением на героя, — ноль при cos = 0,6 (≈53°),
        /// полная при точном совпадении. Раньше тело шло прямо к герою, пока
        /// голова ещё доворачивала, и Вендиго ехал боком.
        /// </summary>
        public static readonly Fix64 WendigoWalkAlignFrom = Fix64.Ratio(6, 10);

        private static readonly Fix64 WendigoClawCos = Fix64.Ratio(34202, 100000);
        private readonly WendigoActionState[] _wendigoActions;
        private readonly int[] _wendigoNextLeap;

        // Перезарядка воя. Массив заводится при первой расстановке, а не в
        // конструкторе: конструктор живёт в Simulation.cs, а вой — здесь.
        // Расстановка — не бой, аллокация там допустима.
        private int[] _wendigoNextHowl;
        private int _wendigoSerial;

        private int[] WendigoNextHowl => _wendigoNextHowl ??= new int[Entities.Capacity];

        /// <summary>
        /// Сколько ещё шагов герой пройдёт замедленным. Прежнее имя для HUD,
        /// вида героя и тестов воя: замедление теперь общее (Simulation.HeroSlow),
        /// и вой лишь одно из того, что его вешает.
        /// </summary>
        public int WendigoHowlSlowTicksLeft => HeroSlowTicksLeft;

        public bool TryGetWendigoAction(int entity, out WendigoActionState action)
        {
            action = (uint)entity < (uint)_wendigoActions.Length ? _wendigoActions[entity] : default;
            return action.Serial != 0;
        }

        public bool IsWendigoAirborne(int id)
        {
            var a = _wendigoActions[id];
            return a.Kind == WendigoAction.Leap && Tick >= a.LaunchTick && Tick <= a.ImpactTick
                && Entities.Alive[id] && !Statuses.IsStunned(id, Tick);
        }

        /// <summary>
        /// Урон когтя — урон листа статов, как у любого удара: глубина,
        /// «Сложно» и ярость приходят в него сами. Раньше здесь стояли
        /// литералы 45/60, и Вендиго на восьмой арене бил как на первой.
        /// </summary>
        public int WendigoClawDamageOf(int id) => Entities.Damage[id];

        /// <summary>Прыжок — коготь × 42/32, доля из таблицы видов.</summary>
        public int WendigoLeapDamageOf(int id)
            => EnemyArchetypes.WendigoShare(Entities.Damage[id], EnemyArchetypes.WendigoLeapDamage);

        /// <summary>«Вой чащи» — коготь × 38/32, доля из таблицы видов.</summary>
        public int WendigoHowlDamageOf(int id)
            => EnemyArchetypes.WendigoShare(Entities.Damage[id], EnemyArchetypes.WendigoHowlDamage);

        /// <summary>Кольцо воя с центром в center — одна фигура и для метки, и для удара.</summary>
        public static EnemyTelegraph WendigoHowlRing(FixVec2 center)
            => EnemyTelegraph.Ring(center, WendigoHowlInnerRadius, WendigoHowlOuterRadius);

        /// <summary>
        /// Держит ли Вендиго крупный жетон: прыжок — до приземления, вой — до
        /// удара. Коготь жетона не берёт. Для подсчёта в BigAttackTokenFree.
        /// </summary>
        internal bool WendigoHoldsBigToken(int id)
        {
            var a = _wendigoActions[id];
            return a.Serial != 0 && a.Kind != WendigoAction.Claw && Tick <= a.ImpactTick;
        }

        /// <summary>
        /// Сдвигает перезарядки прыжка и воя. Для стендов съёмки и тестов:
        /// показать вой, не дожидаясь первого прыжка.
        /// </summary>
        public void SetWendigoCooldowns(int id, int leapReadyTick, int howlReadyTick)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestWendigo) return;
            _wendigoNextLeap[id] = leapReadyTick; WendigoNextHowl[id] = howlReadyTick;
        }

        private void ResetWendigo()
        {
            Array.Clear(_wendigoActions, 0, _wendigoActions.Length);
            Array.Clear(_wendigoNextLeap, 0, _wendigoNextLeap.Length);
            Array.Clear(WendigoNextHowl, 0, WendigoNextHowl.Length); _wendigoSerial = 0;
            // Замедление героя воем — общее (ResetHeroSlow), здесь его нет.
        }

        /// <summary>
        /// Замедление героя воем: минус WendigoHowlSlowPercent на
        /// WendigoHowlSlowTicks его шагов. Модификатор общий (ApplyHeroSlow):
        /// повторный вой не складывается, а продлевает, корни Корнехвата
        /// поверх воя — сильнейшее из двух.
        /// </summary>
        private void ApplyWendigoHowlSlow() => ApplyHeroSlow(WendigoHowlSlowPercent, WendigoHowlSlowTicks);

        private void ConfigureWendigo(int id)
        {
            Entities.BodyRadius[id] = WendigoBodyRadius;
            Entities.PushWeight[id] = Fix64.Ratio(1, 2);
            var s = Entities.Stats[id];
            s.SetBase(StatType.MoveSpeed, Fix64.FromInt(3));
            // Урон листа — коготь; прыжок и вой считаются от него (WendigoLeapDamageOf).
            s.SetBase(StatType.Damage, Fix64.FromInt(EnemyArchetypes.Get(EnemyKind.ForestWendigo).BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.One);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            Entities.XpReward[id] = Progression.EliteKillXp;
            // Сначала сближается пешком; первое появление не даёт бесплатный мгновенный прыжок.
            _wendigoNextLeap[id] = Tick + WendigoLeapCooldownTicks;
            // И бесплатного воя тоже: первый — не раньше полной перезарядки,
            // то есть после первого прыжка. Так крупные атаки чередуются.
            WendigoNextHowl[id] = Tick + WendigoHowlCooldownTicks;
        }

        private void MoveWendigo(int id, FixVec2 toPlayer)
        {
            var a = _wendigoActions[id];
            if (a.Serial != 0)
            {
                Entities.Facing[id] = a.Direction;
                Entities.Velocity[id] = FixVec2.Zero;
                if (a.Kind == WendigoAction.Leap && Tick >= a.LaunchTick && Tick <= a.ImpactTick)
                {
                    var t = Fix64.Clamp(Fix64.Ratio(Tick - a.LaunchTick, 9), Fix64.Zero, Fix64.One);
                    var next = a.Origin + (a.Target - a.Origin) * t;
                    Entities.Velocity[id] = next - Entities.Position[id];
                    Entities.Position[id] = next;
                }
                return;
            }
            Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer, EnemyTurnStepCos, EnemyTurnStepSin);
            if (!UpdateAggro(id, toPlayer)) { Entities.Velocity[id] = FixVec2.Zero; return; }
            // Сначала разворот, потом шаг — и только вдоль взгляда (см. WendigoWalkAlignFrom).
            var facing = Entities.Facing[id]; var step = Entities.MoveStep[id];
            Fix64 wanted = Fix64.Zero;
            if (toPlayer.LengthSq > WendigoClawRange * WendigoClawRange)
            {
                var share = (FixVec2.Dot(facing, toPlayer.Normalized()) - WendigoWalkAlignFrom)
                    / (Fix64.One - WendigoWalkAlignFrom);
                if (share > Fix64.Zero) wanted = step * Fix64.Min(share, Fix64.One);
            }
            // Разгон тот же, что у всех (AccelerationTicks), но скаляром вдоль
            // взгляда: боковой остаток прошлой скорости не доживает ни тика.
            var speed = Fix64.Max(Fix64.Zero, FixVec2.Dot(Entities.Velocity[id], facing));
            var change = step / AccelerationTicks;
            speed = change.Raw <= 0 ? wanted : speed + Fix64.Clamp(wanted - speed, -change, change);
            var from = Entities.Position[id];
            Entities.Position[id] = MoveInsideLayout(id, from, facing * speed);
            Entities.Velocity[id] = Entities.Position[id] - from;
        }

        private bool WendigoLeapPathClear(int id, FixVec2 target)
        {
            var start = Entities.Position[id];
            // Проверяем всю полосу тела, а не одну конечную точку за стеной.
            for (int k = 1; k <= 28; k++)
            {
                var next = start + (target - start) * Fix64.Ratio(k, 28);
                var from = start + (target - start) * Fix64.Ratio(k - 1, 28);
                if ((MoveInsideLayout(id, from, next - from) - next).LengthSq > Fix64.Ratio(1, 10000)) return false;
            }
            return true;
        }

        private void UpdateWendigo()
        {
            // Замедление воя снимает первая стадия тика (ExpireHeroSlow): оно
            // переживает и сам вой, и смерть Вендиго.
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestWendigo) continue;
                var a = _wendigoActions[id];
                if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick)
                    || ForcedMotion.IsActive(Entities, id))
                {
                    if (a.Serial != 0) _events.Add(new SimEvent(SimEventType.WendigoCancelled, id, PlayerId,
                        a.Serial, false, Entities.Position[id], actionVariant: (int)a.Kind));
                    _wendigoActions[id] = default;
                    CancelTelegraphsOf(id);
                    continue;
                }
                if (a.Serial != 0 && Tick >= a.EndTick) { _wendigoActions[id] = default; a = default; }
                if (a.Serial == 0)
                {
                    if (!Entities.Aggro[id] || Tick < Entities.NextAttackTick[id]) continue;
                    var delta = Entities.Position[PlayerId] - Entities.Position[id];
                    var distance = delta.Length;
                    if (distance < Fix64.Ratio(1, 100)) continue;
                    WendigoAction kind = WendigoAction.None;
                    // Прыжок и вой — крупные атаки: без жетона Вендиго идёт пешком или бьёт когтем.
                    // Вой — только пока прыжок перезаряжается: два крупных удара чередуются,
                    // и вой не отнимает у прыжка его дистанцию.
                    if (distance >= Fix64.FromInt(3) && distance <= Fix64.FromInt(7)
                        && Tick >= _wendigoNextLeap[id] && BigAttackTokenFree(id)
                        && WendigoLeapPathClear(id, Entities.Position[PlayerId]))
                        kind = WendigoAction.Leap;
                    else if (distance >= WendigoHowlMinDistance && distance <= WendigoHowlMaxDistance
                        && Tick < _wendigoNextLeap[id] && Tick >= WendigoNextHowl[id] && BigAttackTokenFree(id))
                        kind = WendigoAction.Howl;
                    else if (distance <= WendigoClawRange) kind = WendigoAction.Claw;
                    if (kind == WendigoAction.None) continue;
                    a = new WendigoActionState(++_wendigoSerial, kind, Tick, Entities.Position[id],
                        Entities.Position[PlayerId], delta.Normalized());
                    _wendigoActions[id] = a; Entities.Facing[id] = a.Direction; Entities.Velocity[id] = FixVec2.Zero;
                    // Пауза не продлевает позу восстановления: в ней можно идти и разворачиваться.
                    Entities.NextAttackTick[id] = a.EndTick + (kind == WendigoAction.Claw ? WendigoClawRestTicks : 0);
                    if (kind == WendigoAction.Leap) _wendigoNextLeap[id] = Tick + WendigoLeapCooldownTicks;
                    if (kind == WendigoAction.Howl) WendigoNextHowl[id] = Tick + WendigoHowlCooldownTicks;
                    _events.Add(new SimEvent(SimEventType.WendigoStarted, id, PlayerId, a.Serial,
                        false, kind == WendigoAction.Leap ? a.Target : a.Origin, actionVariant: (int)kind));
                    // Коготь и прыжок — в общем списке без SharedView: на земле их пока
                    // рисует собственный вид Вендиго. Кольцо воя — первая метка Вендиго,
                    // которую рисует общий GroundTelegraphView.
                    if (kind == WendigoAction.Howl)
                        OpenTelegraph(id, WendigoHowlRing(a.Origin), a.ImpactTick,
                            a.ImpactTick + TelegraphLingerTicks, TelegraphFlags.SharedView);
                    else
                        OpenTelegraph(id, kind == WendigoAction.Leap
                                ? EnemyTelegraph.Circle(a.Target, WendigoLeapRadius)
                                : EnemyTelegraph.Sector(a.Origin, a.Direction, WendigoClawRange, WendigoClawCos),
                            a.ImpactTick, a.ImpactTick + TelegraphLingerTicks, TelegraphFlags.None);
                }
                if (a.HitResolved || Tick < a.ImpactTick) continue;
                _wendigoActions[id] = a.Resolve();
                ResolveTelegraphsOf(id);
                _events.Add(new SimEvent(SimEventType.WendigoImpact, id, PlayerId, a.Serial,
                    false, a.Kind == WendigoAction.Leap ? a.Target : a.Origin, actionVariant: (int)a.Kind));
                var offset = Entities.Position[PlayerId] - (a.Kind == WendigoAction.Leap ? a.Target : a.Origin);
                var limit = (a.Kind == WendigoAction.Leap ? WendigoLeapRadius : WendigoClawRange) + Entities.BodyRadius[PlayerId];
                // Вой бьёт ровно по нарисованному кольцу: та же фигура, что в общем списке.
                bool hit = a.Kind == WendigoAction.Howl
                    ? TelegraphContains(WendigoHowlRing(a.Origin), Entities.Position[PlayerId], Entities.BodyRadius[PlayerId])
                    : offset.LengthSq <= limit * limit && (a.Kind == WendigoAction.Leap
                        || FixVec2.WithinArc(a.Direction, offset, WendigoClawCos));
                if (hit && _layout != null)
                {
                    var from = a.Kind == WendigoAction.Leap ? a.Target : a.Origin;
                    var ray = Entities.Position[PlayerId] - from;
                    int steps = Math.Max(1, (ray.Length / Fix64.Ratio(1, 5)).ToInt() + 1);
                    for (int step = 1; step <= steps && hit; step++)
                        hit = _layout.IsWalkable(from + ray * Fix64.Ratio(step, steps), Fix64.Ratio(1, 10));
                }
                if (!hit) continue;
                int health = Entities.Health[PlayerId];
                ApplyAbilityDamage(id, PlayerId, a.Kind == WendigoAction.Leap ? WendigoLeapDamageOf(id)
                    : a.Kind == WendigoAction.Howl ? WendigoHowlDamageOf(id) : WendigoClawDamageOf(id),
                    -1, DamageType.Physical);
                // Замедление — только если вой действительно достал: уклонение,
                // неуязвимость и отложенный Песочными Часами урон его не вешают.
                // То же правило, что у отброса Камнекопыта.
                if (a.Kind == WendigoAction.Howl && Entities.Alive[PlayerId] && Entities.Health[PlayerId] < health)
                    ApplyWendigoHowlSlow();
            }
        }

        public EncounterPlan SetupWendigoEncounter(LayoutMap map, ulong seed, bool withPack = false)
        {
            // Здоровье стенда — табличное: стенд показывает того же Вендиго, что забег.
            int health = ArchetypeHealth(EnemyKind.ForestWendigo);
            if (map == null) SetupTestArena(0); else SetupRift(map, seed, 0, 0, health);
            _campWalkMap = null; _events.Clear();
            FindForestBudTestStage(map, out var hero, out var enemy, out int module);
            // Середина поляны удобнее края тропы: кроны не заслоняют телеграф.
            if (map != null)
                for (int m = 1; m < map.PlacedCount; m++)
                {
                    if (map.IsExit(m)) continue;
                    var center = map.CenterOf(m);
                    var axis = new FixVec2(Fix64.FromInt(3), Fix64.Zero);
                    var a = center - axis; var b = center + axis;
                    if (!map.CanTravel(a, b, Fix64.FromInt(2))
                        || !map.CanTravel(center - new FixVec2(Fix64.Zero, Fix64.FromInt(3)),
                            center + new FixVec2(Fix64.Zero, Fix64.FromInt(3)), Fix64.FromInt(2))) continue;
                    hero = a; enemy = b; module = m; break;
                }
            var direction = (enemy - hero).Normalized();
            enemy = hero + direction * Fix64.FromInt(6);
            if (map != null) enemy = map.ClampToWalkable(enemy, WendigoBodyRadius);
            Entities.Position[PlayerId] = hero; Entities.Facing[PlayerId] = direction;
            int id = Entities.Spawn(enemy, health, Faction.Orvill); ConfigureEnemy(id, EnemyKind.ForestWendigo);
            Entities.Aggro[id] = true; Entities.Facing[id] = -direction; Entities.NextAttackTick[id] = Tick;
            _events.Add(SimEvent.Spawn(id, enemy));
            var elite = new bool[Entities.Capacity]; elite[id] = true; _eliteMask = elite;
            if (withPack)
                for (int n = 0; n < 2; n++)
                {
                    var side = new FixVec2(-direction.Y, direction.X) * Fix64.FromInt(n == 0 ? -2 : 2);
                    var point = enemy + side;
                    if (map != null) point = map.ClampToWalkable(point, Fix64.Ratio(45, 100));
                    int ally = Entities.Spawn(point, ArchetypeHealth(EnemyKind.ForestRootSwarm), Faction.Orvill);
                    ConfigureEnemy(ally, EnemyKind.ForestRootSwarm);
                    Entities.Aggro[ally] = true; _events.Add(SimEvent.Spawn(ally, point));
                }
            Grid.Rebuild(Entities);
            return new EncounterPlan(new List<EncounterPlacement> { new EncounterPlacement(EncounterRole.MainPath,
                module, -1, StableId.Of("encounter.forest-wendigo.test"), enemy, 1, withPack ? 3 : 1) }, elite, Fix64.FromInt(5), 0);
        }

        private void HashWendigo(ref ulong hash)
        {
            bool present = _wendigoSerial != 0;
            for (int id = 1; id < Entities.Count && !present; id++) present = Entities.Kind[id] == EnemyKind.ForestWendigo;
            if (!present) return;
            Hashing.Mix(ref hash, 0x57454E44); Hashing.Mix(ref hash, _wendigoSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestWendigo) continue;
                var a = _wendigoActions[id]; Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, _wendigoNextLeap[id]);
                Hashing.Mix(ref hash, WendigoNextHowl[id]);
                Hashing.Mix(ref hash, a.Serial); Hashing.Mix(ref hash, (int)a.Kind); Hashing.Mix(ref hash, a.StartTick);
                Hashing.Mix(ref hash, a.Origin.X.Raw); Hashing.Mix(ref hash, a.Origin.Y.Raw);
                Hashing.Mix(ref hash, a.Target.X.Raw); Hashing.Mix(ref hash, a.Target.Y.Raw);
                Hashing.Mix(ref hash, a.Direction.X.Raw); Hashing.Mix(ref hash, a.Direction.Y.Raw);
                Hashing.Mix(ref hash, a.HitResolved ? 1 : 0);
            }
        }
    }
}
