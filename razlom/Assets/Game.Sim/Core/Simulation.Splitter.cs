using System;

namespace Game.Sim
{
    /// <summary>
    /// Состояние Расщепеня и его детёныша на сущность. Изменяемая структура:
    /// поля добавляет этот файл, и каждое новое обязано попасть в HashSplitters.
    /// </summary>
    public struct SplitterState
    {
        /// <summary>У детёныша — индекс родителя; 0 — не из распада (герой родителем не бывает).</summary>
        public int Parent;

        // ---- перекат клубком (только ForestSplitter) ----

        /// <summary>Моб впервые увидел героя, и RollReadyTick отсчитан. Переживает перекат.</summary>
        public bool RollArmed;

        /// <summary>С какого тика можно катиться: первый раз — через 60 после агро, дальше — 180 от сжатия.</summary>
        public int RollReadyTick;

        /// <summary>Номер переката; 0 — переката нет.</summary>
        public int RollSerial;

        /// <summary>Сжатие, фиксация полосы, пуск. Ставятся в тик сжатия.</summary>
        public int RollStartTick, RollLockTick, RollLaunchTick;

        /// <summary>Остановка и конец раскрытия (оглушения). До фиксации — 0, до остановки — плановые.</summary>
        public int RollStopTick, RollEndTick;

        /// <summary>Сколько тиков катится по полосе (1…16). Считается при фиксации.</summary>
        public int RollTicks;

        /// <summary>Номер метки полосы; 0 — полосы ещё нет (или пул был полон).</summary>
        public int RollTelegraphSerial;

        /// <summary>Начало полосы и её направление. До фиксации — место и взгляд в тик сжатия.</summary>
        public FixVec2 RollOrigin, RollDirection;

        /// <summary>Где был центр клубка в начале этого тика — начало протяжки попадания.</summary>
        public FixVec2 RollPrevious;

        /// <summary>Длина полосы и сколько уже прокатился, метры.</summary>
        public Fix64 RollLength, RollTravelled;

        /// <summary>Полоса зафиксирована (метка открыта), клубок остановился.</summary>
        public bool RollLocked, RollStopped;

        /// <summary>Полосу обрезала стена: в её конце клубок бьётся о стену.</summary>
        public bool RollWallAhead;

        /// <summary>Шаг упёрся в стену раньше конца полосы (ход в MoveSplitter, остановка — в UpdateSplitters).</summary>
        public bool RollBlocked;

        /// <summary>Остановился о стену: оглушение вместо раскрытия.</summary>
        public bool RollWallStop;

        /// <summary>Единственный удар переката по герою уже был.</summary>
        public bool RollHitResolved;
    }

    /// <summary>Фаза переката Расщепеня. Значение идёт в вид — новые только в конец.</summary>
    public enum SplitterRollPhase : byte
    {
        None = 0,

        /// <summary>Сжатие: стоит, сворачивается и доворачивается к герою. Метки нет.</summary>
        Curl = 1,

        /// <summary>Полоса зафиксирована и нарисована, клубок стоит (вид трясёт его с тика 24).</summary>
        Locked = 2,

        /// <summary>Катится по полосе.</summary>
        Rolling = 3,

        /// <summary>Остановился в конце полосы и раскрывается: стоит, окно наказания.</summary>
        Uncurl = 4,

        /// <summary>Врезался в стену: оглушён.</summary>
        Dizzy = 5,
    }

    /// <summary>
    /// Перекат Расщепеня для вида: по нему идут клипы Curl/RollLoop/Uncurl/Dizzy
    /// и борозда в траве. Снимок на текущий тик — вид ничего не пересчитывает.
    /// </summary>
    public readonly struct SplitterRollState
    {
        /// <summary>Номер переката: сменился — новый перекат, клипы с начала.</summary>
        public readonly int Serial;

        /// <summary>Фаза на текущий тик (Simulation.Tick).</summary>
        public readonly SplitterRollPhase Phase;

        /// <summary>Сжатие; фиксация полосы (StartTick + 12); пуск (StartTick + 30).</summary>
        public readonly int StartTick, LockTick, LaunchTick;

        /// <summary>
        /// Тик остановки и конец раскрытия или оглушения. До фиксации — 0; до
        /// остановки — плановые (конец полосы), стена посреди пути их переписывает.
        /// </summary>
        public readonly int StopTick, EndTick;

        /// <summary>Начало полосы и направление качения. До фиксации — место и взгляд в тик сжатия.</summary>
        public readonly FixVec2 Origin, Direction;

        /// <summary>Длина полосы (0 до фиксации) и сколько клубок уже прокатился — для борозды и вращения.</summary>
        public readonly Fix64 Length, Travelled;

        /// <summary>Удар по герою уже был: второго в этом перекате нет.</summary>
        public readonly bool HitResolved;

        /// <summary>Остановка о стену (или она впереди — полосу обрезала стена): в конце — Dizzy.</summary>
        public readonly bool WallStop;

        /// <summary>Номер метки полосы в общем списке (Simulation.FindTelegraph); 0 — нет.</summary>
        public readonly int TelegraphSerial;

        public SplitterRollState(int serial, SplitterRollPhase phase, int startTick, int lockTick, int launchTick,
            int stopTick, int endTick, FixVec2 origin, FixVec2 direction, Fix64 length, Fix64 travelled,
            bool hitResolved, bool wallStop, int telegraphSerial)
        {
            Serial = serial; Phase = phase; StartTick = startTick; LockTick = lockTick; LaunchTick = launchTick;
            StopTick = stopTick; EndTick = endTick; Origin = origin; Direction = direction; Length = length;
            Travelled = travelled; HitResolved = hitResolved; WallStop = wallStop; TelegraphSerial = telegraphSerial;
        }
    }

    /// <summary>
    /// РАСЩЕПЕНЬ И ЕГО ДЕТЁНЫШ (план новых мобов от 26.09).
    ///
    /// Бьют общим ближним замахом (Simulation.EnemyMelee), числа замаха —
    /// здесь: Расщепень — сектор 1,8 м / 90°, замах 18, стойка 12, ближний
    /// жетон, как Хранитель; детёныш — укус, как Корнеполз, с жетоном укуса.
    /// Ходят общим ходом ближника (MoveSplitter отдаёт ход MoveEnemies).
    ///
    /// ПЕРЕКАТ КЛУБКОМ (одобрен владельцем 27.09: «видно сжатие, потом катится
    /// вперёд по полосе, от него можно увернуться»). Только Расщепень, детёныш
    /// не катается. Старт (UpdateSplitters, по возрастанию индекса): агро, не
    /// встаёт из земли, не оглушён, не волочим, замаха нет, своя перезарядка
    /// готова (первый раз — через 60 тиков после агро), герой в 3–7 м между
    /// центрами и в ±30° от взгляда, ближний жетон свободен, крупная метка
    /// умещается в бюджет (BigMarkAllowed, вес 1, удар на 30-м тике) и 3 м
    /// полосы к герою свободны от стен. Вплотную (до 1,8 м) бьёт замахом, а
    /// перекат ищет от 3 м.
    ///
    /// Тики от сжатия t0: 0–12 сжатие — стоит, доворачивается к герою на 8° за
    /// тик, метки нет (EnemyActionStarted). На 12-м полоса фиксируется по
    /// взгляду: метка Lane 1,6 м шириной, длиной min(свободно от стен, до
    /// героя + 2, 6,5) без SharedView — борозду рисует свой вид. Короче 3 м —
    /// отмена. 12–30 стоит (вид трясёт клубок с 24-го). На 30-м пуск
    /// (EnemyActionImpact, stage 0): 0,4 м за тик до конца полосы, не больше
    /// 16 тиков, без скольжения вдоль стен и без расталкивания. Удар один на
    /// перекат: протяжка тела по пути этого тика против тела героя, и только
    /// внутри нарисованной полосы — 18/12 урона листа и отброс вбок на 1,2 м,
    /// если здоровье действительно ушло; клубок катится дальше сквозь героя.
    /// Стоп (EnemyActionImpact, stage 1, Flag — задел): раскрытие 30 тиков —
    /// стоит, окно наказания; о стену — оглушение 45. Ближний жетон и вес
    /// метки — от сжатия до остановки. Перезарядка 180 от сжатия. Оглушение
    /// (кроме своего о стену), волок, смерть моба или героя до остановки
    /// снимают перекат (EnemyActionCancelled) и его метку; перезарядка остаётся.
    ///
    /// Распад: Kill ставит умершего Расщепеня в очередь (QueueSplit) — только
    /// настоящую смерть: уход в землю по концу выживания и Alive = false в
    /// тестах идут мимо Kill, а детёныш сам не делится. ResolvePendingSplits
    /// зовётся в Step после TickIgnite и до UpdateEncounterWaves: дети встают
    /// на 12-м тике смерти. HasPendingSplits держит встречу до настоящего появления детей.
    ///
    /// Дети — два детёныша по бокам от взгляда родителя. Здоровье и урон — доля
    /// родителя (160/560 и 4/12), поэтому глубина, «Сложно» и подстройка пачки
    /// доезжают до детей сами. Опыт 5, элитой не бывают. Их выбрасывает на метр
    /// за 8 тиков (ForcedMotionKind.SplitPop — не помеха укусу), и первые 15
    /// тиков они не кусают: иначе смерть Расщепеня вплотную к герою била бы
    /// двумя укусами без всякой позы.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- замах Расщепеня (профиль — MeleeProfileOf) ----
        public const int SplitterSwingWindupTicks = 18, SplitterSwingRecoveryTicks = 12;
        public const int SplitterSwingCycleTicks = 42;
        public static readonly Fix64 SplitterSwingRadius = Fix64.Ratio(9, 5);
        public static readonly Fix64 SplitterSwingArcCos = Fix64.Ratio(70711, 100000);   // ±45°
        public static readonly Fix64 SplitterSwingCommitCos = Fix64.Ratio(4, 5);         // старт, как у Хранителя
        public static readonly Fix64 SplitterChaseRange = Fix64.Ratio(8, 5);             // подходит на 1,6 м
        public static readonly Fix64 SplitterMoveSpeed = Fix64.Ratio(31, 10);

        // ---- распад ----
        /// <summary>Сколько детёнышей даёт Расщепень. Им же EnemyArchetypes.BodiesPerSpawn считает места.</summary>
        public const int SplitChildren = 2;

        /// <summary>Кадр 12 клипа Death при 30 fps: настоящий распад через 0,4 с после смерти.</summary>
        public const int SplitterDeathReleaseTicks = 12;

        /// <summary>За сколько тиков детёныша выбрасывает из тела родителя.</summary>
        public const int SplitPopTicks = 8;

        /// <summary>На сколько метров выбрасывает — от точки появления, вбок от взгляда родителя.</summary>
        public static readonly Fix64 SplitPopDistance = Fix64.One;

        /// <summary>Где встаёт детёныш: вбок от центра родителя, метры.</summary>
        public static readonly Fix64 SplitSideOffset = Fix64.Ratio(1, 10);

        // ---- детёныш ----
        public const int SplitlingBiteWindupTicks = 12, SplitlingBiteRecoveryTicks = 8;
        public const int SplitlingBiteCycleTicks = 30;
        /// <summary>Сколько тиков после появления детёныш не кусает.</summary>
        public const int SplitlingSpawnGuardTicks = 15;
        public const int SplitlingKillXp = 5;
        public static readonly Fix64 SplitlingBiteRange = Fix64.Ratio(7, 5);
        public static readonly Fix64 SplitlingMoveSpeed = Fix64.Ratio(21, 5);
        public static readonly Fix64 SplitlingRushSpeed = Fix64.FromInt(5);

        // ---- перекат клубком ----

        /// <summary>Сжатие: столько тиков от старта до фиксации полосы. Метки ещё нет.</summary>
        public const int SplitterRollCurlTicks = 12;

        /// <summary>От старта до пуска: полоса лежит на земле 18 тиков до того, как клубок покатится.</summary>
        public const int SplitterRollLaunchTicks = 30;

        /// <summary>Дольше этого клубок не катится.</summary>
        public const int SplitterRollMaxTicks = 16;

        /// <summary>Раскрытие после остановки: стоит — окно наказания.</summary>
        public const int SplitterRollUncurlTicks = 30;

        /// <summary>Оглушение после удара о стену.</summary>
        public const int SplitterRollDizzyTicks = 45;

        /// <summary>Перезарядка переката от тика сжатия. Снятый перекат её не обнуляет.</summary>
        public const int SplitterRollCooldownTicks = 180;

        /// <summary>Первый перекат — не раньше стольких тиков после того, как моб заметил героя.</summary>
        public const int SplitterRollFirstDelayTicks = 30;

        /// <summary>
        /// Перекат готов — Расщепень не лезет в укус, а отходит на это кольцо вокруг
        /// героя (Simulation.EnemySurround) и катится оттуда: вплотную (ближе 3 м)
        /// катиться нельзя, и без отхода перекат в ближнем бою не случался бы вовсе.
        /// </summary>
        public static readonly Fix64 SplitterRollStandoff = Fix64.Ratio(9, 2);

        /// <summary>Разворот в сжатии: 8° за тик.</summary>
        public const int SplitterRollTurnDegreesPerTick = 8;

        /// <summary>Урон переката — доля урона листа: 18 при табличных 12.</summary>
        public const int SplitterRollDamage = 18;

        /// <summary>Тиков на отброс героя.</summary>
        public const int SplitterRollKnockbackTicks = 6;

        /// <summary>Скорость качения: 0,4 м за тик (12 м/с).</summary>
        public static readonly Fix64 SplitterRollSpeed = Fix64.Ratio(2, 5);

        /// <summary>Ширина полосы на земле — чуть шире тела (1,4 м).</summary>
        public static readonly Fix64 SplitterRollLaneWidth = Fix64.Ratio(8, 5);

        /// <summary>Длиннее полоса не бывает.</summary>
        public static readonly Fix64 SplitterRollMaxLength = Fix64.Ratio(13, 2);

        /// <summary>Полоса дальше героя на столько метров: клубок прокатывается насквозь.</summary>
        public static readonly Fix64 SplitterRollOvershoot = Fix64.FromInt(2);

        /// <summary>
        /// Герой между центрами не ближе и не дальше этого. Короче 3 м полосы
        /// тоже не бывает: и на старте, и при фиксации.
        /// </summary>
        public static readonly Fix64 SplitterRollMinDistance = Fix64.FromInt(3), SplitterRollMaxDistance = Fix64.FromInt(7);

        /// <summary>Старт — только когда герой в ±30° от взгляда.</summary>
        public static readonly Fix64 SplitterRollStartCos = Fix64.Ratio(86603, 100000);

        /// <summary>Отброс героя вбок от полосы при попадании.</summary>
        public static readonly Fix64 SplitterRollKnockback = Fix64.Ratio(6, 5);

        private static readonly Fix64 SplitterRollTurnStep = Fix64.TwoPi / (360 / SplitterRollTurnDegreesPerTick);
        private static readonly Fix64 SplitterRollTurnStepCos = Fix64.Cos(SplitterRollTurnStep);
        private static readonly Fix64 SplitterRollTurnStepSin = Fix64.Sin(SplitterRollTurnStep);

        // Шаг пробы полосы: 10 см, дальше — деление пополам. CanTravel на
        // карте из комнат проверяет только конец отрезка, поэтому длинный
        // отрезок одной пробой проскочил бы тонкую стену.
        private static readonly Fix64 SplitterRollProbeStep = Fix64.Ratio(1, 10);

        private readonly SplitterState[] _splitters;
        private int _splitterRollSerial;

        // Очередь распада: снимок смерти и тик выпуска, в порядке смерти.
        // Размер — ёмкость пула: индекс ставится не больше раза, так что места
        // хватает всегда, и в бою очередь не растёт.
        private readonly struct PendingSplitterSplit
        {
            public readonly int Parent, ReleaseTick, Health, Damage;
            public readonly FixVec2 Position, Facing;
            public PendingSplitterSplit(int parent, int releaseTick, int health, int damage, FixVec2 position, FixVec2 facing)
            { Parent = parent; ReleaseTick = releaseTick; Health = health; Damage = damage; Position = position; Facing = facing; }
        }
        private readonly PendingSplitterSplit[] _pendingSplits;
        private int _pendingSplitCount;

        public int PendingSplitCount => _pendingSplitCount;
        public bool HasPendingSplits => _pendingSplitCount != 0;
        public bool HasPendingSplitFor(int parent)
        {
            for (int k = 0; k < _pendingSplitCount; k++)
                if (_pendingSplits[k].Parent == parent) return true;
            return false;
        }

        /// <summary>Родитель детёныша или -1: не детёныш или поставлен не распадом.</summary>
        public int SplitParentOf(int id)
            => (uint)id < (uint)Entities.Count && _splitters[id].Parent > 0 ? _splitters[id].Parent : -1;

        private void ResetSplitters()
        {
            Array.Clear(_splitters, 0, _splitters.Length);
            Array.Clear(_pendingSplits, 0, _pendingSplits.Length);
            _pendingSplitCount = 0;
            _splitterRollSerial = 0;
        }

        private void ConfigureSplitter(int id)
        {
            var archetype = EnemyArchetypes.Get(EnemyKind.ForestSplitter);
            Entities.BodyRadius[id] = archetype.BodyRadius;
            Entities.PushWeight[id] = Fix64.One;
            var s = Entities.Stats[id];
            s.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.Ratio(TicksPerSecond, SplitterSwingCycleTicks));
            s.SetBase(StatType.MoveSpeed, SplitterMoveSpeed);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            _splitters[id] = default;
        }

        /// <summary>
        /// Детёныш по строке таблицы. Распад поверх этого ставит здоровье и
        /// урон от родителя, родителя, выброс и паузу перед первым укусом.
        /// </summary>
        private void ConfigureSplitling(int id)
        {
            var archetype = EnemyArchetypes.Get(EnemyKind.ForestSplitling);
            Entities.BodyRadius[id] = archetype.BodyRadius;
            Entities.PushWeight[id] = Fix64.FromInt(2);
            var s = Entities.Stats[id];
            s.SetBase(StatType.Damage, Fix64.FromInt(archetype.BaseDamage));
            s.SetBase(StatType.AttackSpeed, Fix64.Ratio(TicksPerSecond, SplitlingBiteCycleTicks));
            s.SetBase(StatType.MoveSpeed, SplitlingMoveSpeed);
            s.SetBase(StatType.CritChance, Fix64.Zero);
            s.SetBase(StatType.CritMultiplier, Fix64.One);
            Entities.RefreshStats(id); Entities.Health[id] = Entities.MaxHealth[id];
            Entities.XpReward[id] = SplitlingKillXp;
            _splitters[id] = default;
        }

        /// <summary>
        /// Ход Расщепеня и детёныша. true — ход сделан целиком; false — общий
        /// ход ближника в MoveEnemies (подход, жетоны, кружение, рывок детёныша
        /// вплотную). Свой ход у Расщепеня только в перекате: сжатие —
        /// стоит и доворачивается, дальше — стоит по полосе, катится по ней,
        /// стоит в раскрытии. Детёныш не катается и ходит всегда общим ходом.
        /// </summary>
        private bool MoveSplitter(int id, FixVec2 toPlayer)
        {
            var s = _splitters[id];
            var phase = SplitterRollPhaseOf(in s);
            if (phase == SplitterRollPhase.None) return false;
            Entities.Velocity[id] = FixVec2.Zero;
            switch (phase)
            {
                case SplitterRollPhase.Curl:
                    // Фиксация — в UpdateSplitters этого же тика, после хода:
                    // доворот идёт и в тик фиксации, всего 12 шагов по 8°.
                    if (!s.RollLocked)
                    {
                        Entities.Facing[id] = TurnToward(Entities.Facing[id], toPlayer,
                            SplitterRollTurnStepCos, SplitterRollTurnStepSin);
                        return true;
                    }
                    break;
                case SplitterRollPhase.Rolling:
                    RollSplitter(id);
                    return true;
            }
            // Полоса зафиксирована — корпус смотрит по ней до конца переката.
            Entities.Facing[id] = s.RollDirection;
            return true;
        }

        /// <summary>
        /// Шаг качения. Центр идёт строго по оси полосы: n-й тик — 0,4 × n м от
        /// начала, последний — ровно в её конец. Стена на пути — стоп в
        /// последней свободной точке, без скольжения; саму остановку делает
        /// UpdateSplitters этого тика.
        /// </summary>
        private void RollSplitter(int id)
        {
            var s = _splitters[id];
            var from = Entities.Position[id];
            s.RollPrevious = from;
            Entities.Facing[id] = s.RollDirection;
            if (s.RollBlocked) { _splitters[id] = s; return; }
            int step = Tick - s.RollLaunchTick + 1;
            if (step > s.RollTicks) { _splitters[id] = s; return; }
            Fix64 travel = step >= s.RollTicks ? s.RollLength : Fix64.Min(s.RollLength, SplitterRollSpeed * step);
            var next = s.RollOrigin + s.RollDirection * travel;
            Fix64 radius = Entities.BodyRadius[id];
            if (_layout != null && !_layout.CanTravel(from, next, radius))
            {
                // Последняя свободная точка между from и next — делением пополам.
                var low = from; var high = next;
                for (int j = 0; j < 10; j++)
                {
                    var middle = (low + high) * Fix64.Ratio(1, 2);
                    if (_layout.CanTravel(low, middle, radius)) low = middle; else high = middle;
                }
                next = low;
                s.RollBlocked = true;
            }
            Entities.Position[id] = next;
            Entities.Velocity[id] = next - from;
            s.RollTravelled = FixVec2.Dot(next - s.RollOrigin, s.RollDirection);
            _splitters[id] = s;
        }

        /// <summary>
        /// Перекат Расщепеня. Зовётся в Step после UpdateRootSnarers — после
        /// хода всех и после расталкивания: удар считается по уже сделанному
        /// шагу клубка и по месту героя в этот тик. Обход по возрастанию
        /// индекса: ближний жетон и бюджет меток берёт младший. Замах — общий
        /// (EnemyMelee), распад — ResolvePendingSplits в конце тика.
        /// </summary>
        private void UpdateSplitters()
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestSplitter) continue;
                if (_splitters[id].RollSerial != 0) { UpdateSplitterRoll(id); continue; }
                if (!Entities.Alive[id]) continue;
                // Первый перекат — через 60 тиков после того, как моб заметил героя.
                if (Entities.Aggro[id] && !_splitters[id].RollArmed)
                {
                    _splitters[id].RollArmed = true;
                    _splitters[id].RollReadyTick = Tick + SplitterRollFirstDelayTicks;
                }
                TryStartSplitterRoll(id);
            }
        }

        private void TryStartSplitterRoll(int id)
        {
            var s = _splitters[id];
            if (!s.RollArmed || Tick < s.RollReadyTick || !Entities.Aggro[id] || !Entities.Alive[PlayerId]) return;
            if (IsEmerging(id) || Statuses.IsStunned(id, Tick) || ForcedMotion.IsActive(Entities, id)) return;
            if (TryGetEnemySwing(id, out _)) return;
            var origin = Entities.Position[id];
            var offset = Entities.Position[PlayerId] - origin;
            Fix64 distanceSq = offset.LengthSq;
            if (distanceSq < SplitterRollMinDistance * SplitterRollMinDistance
                || distanceSq > SplitterRollMaxDistance * SplitterRollMaxDistance) return;
            if (!FixVec2.WithinArc(Entities.Facing[id], offset, SplitterRollStartCos)) return;
            if (CountMeleeAttackTokens(id) >= MeleeAttackTokenLimit) return;
            if (!BigMarkAllowed(id, 1, Tick + SplitterRollLaunchTicks)) return;
            var direction = offset.Normalized();
            if (SplitterClearLane(id, origin, direction, SplitterRollMinDistance, out _) < SplitterRollMinDistance) return;

            var facing = Entities.Facing[id].Normalized();
            if (facing.LengthSq.Raw == 0) facing = direction;
            _splitters[id] = new SplitterState
            {
                Parent = s.Parent, RollArmed = true,
                // Перезарядка — от сжатия: снятый перекат её не обнуляет.
                RollReadyTick = Tick + SplitterRollCooldownTicks,
                RollSerial = ++_splitterRollSerial, RollStartTick = Tick,
                RollLockTick = Tick + SplitterRollCurlTicks, RollLaunchTick = Tick + SplitterRollLaunchTicks,
                RollOrigin = origin, RollDirection = facing, RollPrevious = origin,
            };
            Entities.Velocity[id] = FixVec2.Zero;
            // Сжатие — только тело и звук: полосы на земле ещё нет.
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.SplitterRoll, origin));
        }

        /// <summary>Одна стадия переката за тик: снятие, фиксация, пуск, удар, стоп, конец раскрытия.</summary>
        private void UpdateSplitterRoll(int id)
        {
            var s = _splitters[id];
            if (s.RollStopped)
            {
                // Раскрытие и оглушение о стену: удара уже не будет, снимать
                // нечего. Волок, смерть или чужое оглушение в раскрытии просто
                // кончают перекат — вид дальше показывает их.
                bool interrupted = !Entities.Alive[id] || ForcedMotion.IsActive(Entities, id)
                    || (!s.RollWallStop && Statuses.IsStunned(id, Tick));
                if (interrupted || Tick >= s.RollEndTick) EndSplitterRoll(id);
                return;
            }

            if (!Entities.Alive[id] || !Entities.Alive[PlayerId] || Statuses.IsStunned(id, Tick)
                || ForcedMotion.IsActive(Entities, id))
            { CancelSplitterRoll(id); return; }

            if (!s.RollLocked)
            {
                if (Tick >= s.RollLockTick) LockSplitterRoll(id);
                return;
            }
            if (Tick < s.RollLaunchTick) return;

            if (Tick == s.RollLaunchTick)
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                    EnemyActionKind.SplitterRoll, s.RollOrigin, 0, false));

            if (!s.RollHitResolved) ResolveSplitterRollHit(id);
            // Отражённый урон мог убить самого моба — снимет следующий тик.
            if (!Entities.Alive[id]) return;
            s = _splitters[id];
            if (s.RollBlocked || Tick >= s.RollLaunchTick + s.RollTicks - 1) StopSplitterRoll(id);
        }

        /// <summary>
        /// Фиксация на 12-м тике: направление — взгляд после сжатия, полоса —
        /// до стены, до героя + 2 м или 6,5 м, что ближе. Короче 3 м — отмена.
        /// </summary>
        private void LockSplitterRoll(int id)
        {
            var s = _splitters[id];
            var origin = Entities.Position[id];
            var direction = Entities.Facing[id].Normalized();
            if (direction.LengthSq.Raw == 0) direction = s.RollDirection;
            Fix64 reach = Fix64.Min((Entities.Position[PlayerId] - origin).Length + SplitterRollOvershoot,
                SplitterRollMaxLength);
            Fix64 length = SplitterClearLane(id, origin, direction, reach, out bool wall);
            if (length < SplitterRollMinDistance) { CancelSplitterRoll(id); return; }

            // Шагов — длина / 0,4 с округлением вверх, но не больше 16; последний
            // шаг приходит ровно в конец полосы (при 6,5 м он полметра). Миллиметр
            // допуска: длина 6,0000001 м от корня Fix64 — это те же 15 шагов, не 16.
            long slack = Fix64.Ratio(1, 1000).Raw;
            int ticks = (int)((length.Raw - slack + SplitterRollSpeed.Raw - 1) / SplitterRollSpeed.Raw);
            if (ticks > SplitterRollMaxTicks) ticks = SplitterRollMaxTicks;
            if (ticks < 1) ticks = 1;
            s.RollLocked = true;
            s.RollOrigin = origin; s.RollDirection = direction; s.RollPrevious = origin;
            s.RollLength = length; s.RollTicks = ticks; s.RollWallAhead = wall;
            s.RollStopTick = s.RollLaunchTick + ticks - 1;
            s.RollEndTick = s.RollStopTick + (wall ? SplitterRollDizzyTicks : SplitterRollUncurlTicks);
            Entities.Facing[id] = direction;
            // Без SharedView: борозду в траве рисует свой вид Расщепеня.
            int slot = OpenTelegraph(id, SplitterRollLane(origin, direction, length), s.RollLaunchTick,
                s.RollStopTick + TelegraphLingerTicks, TelegraphFlags.None);
            s.RollTelegraphSerial = TryGetTelegraph(slot, out var lane) ? lane.Serial : 0;
            _splitters[id] = s;
        }

        /// <summary>
        /// Удар: протяжка тела по пути этого тика против тела героя. Герой
        /// ходит не быстрее 0,15 м за тик, клубок — 0,4, поэтому протяжки
        /// против неподвижной точки хватает. Бьёт только там, где полоса
        /// нарисована: позади начала и за её концом тело клубка героя не бьёт.
        /// </summary>
        private void ResolveSplitterRollHit(int id)
        {
            var s = _splitters[id];
            var hero = Entities.Position[PlayerId];
            Fix64 heroRadius = Entities.BodyRadius[PlayerId];
            var from = s.RollPrevious;
            var delta = Entities.Position[id] - from;
            var t = delta.LengthSq.Raw == 0 ? Fix64.Zero
                : Fix64.Clamp(FixVec2.Dot(hero - from, delta) / delta.LengthSq, Fix64.Zero, Fix64.One);
            Fix64 reach = Entities.BodyRadius[id] + heroRadius;
            if ((hero - (from + delta * t)).LengthSq > reach * reach) return;
            var lane = SplitterRollLane(s.RollOrigin, s.RollDirection, s.RollLength);
            if (!TelegraphContains(in lane, hero, heroRadius)) return;

            // Состояние пишется ДО урона: отражённый урон может убить моба.
            s.RollHitResolved = true;
            _splitters[id] = s;
            int health = Entities.Health[PlayerId];
            ApplyAbilityDamage(id, PlayerId, SplitterRollDamageOf(id), -1, DamageType.Physical);
            if (!Entities.Alive[PlayerId] || Entities.Health[PlayerId] >= health) return;

            // Отброс вбок от полосы — в ту сторону, где герой стоит; упирается в стену.
            var side = new FixVec2(-s.RollDirection.Y, s.RollDirection.X);
            if (FixVec2.Dot(hero - Entities.Position[id], side) < Fix64.Zero) side = -side;
            var target = hero;
            var piece = SplitterRollKnockback / 24;
            for (int j = 0; j < 24; j++)
            {
                var next = target + side * piece;
                if (_layout != null && !_layout.CanTravel(target, next, heroRadius)) break;
                target = next;
            }
            ForcedMotion.Begin(Entities, PlayerId, target, SplitterRollKnockbackTicks, ForcedMotionKind.Knockback);
        }

        /// <summary>
        /// Стоп в конце полосы или у стены: событие, метка сработала, дальше —
        /// раскрытие 30 тиков или оглушение 45 о стену. Жетон свободен с этого тика.
        /// </summary>
        private void StopSplitterRoll(int id)
        {
            var s = _splitters[id];
            s.RollStopped = true;
            s.RollWallStop = s.RollBlocked || s.RollWallAhead;
            s.RollStopTick = Tick;
            s.RollEndTick = Tick + (s.RollWallStop ? SplitterRollDizzyTicks : SplitterRollUncurlTicks);
            _splitters[id] = s;
            Entities.Velocity[id] = FixVec2.Zero;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.SplitterRoll, Entities.Position[id], 1, s.RollHitResolved));
            ResolveTelegraphsOf(id);
            if (s.RollWallStop) Statuses.ApplyStun(id, s.RollEndTick);
        }

        /// <summary>
        /// Снимает перекат до остановки: событие, метка гаснет, жетон свободен.
        /// Перезарядка остаётся — от сжатия.
        /// </summary>
        private void CancelSplitterRoll(int id)
        {
            var s = _splitters[id];
            if (s.RollSerial == 0) return;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionCancelled, id, PlayerId,
                EnemyActionKind.SplitterRoll, Entities.Position[id]));
            EndSplitterRoll(id);
            Entities.Velocity[id] = FixVec2.Zero;
            // Смерть через Kill метку уже сняла — повторный вызов ничего не найдёт.
            CancelTelegraphsOf(id);
        }

        /// <summary>Стирает перекат, оставляя родителя и перезарядку.</summary>
        private void EndSplitterRoll(int id)
        {
            var s = _splitters[id];
            _splitters[id] = new SplitterState { Parent = s.Parent, RollArmed = s.RollArmed, RollReadyTick = s.RollReadyTick };
            if (s.RollSerial != 0) YieldSurroundSlot(id);
        }

        /// <summary>
        /// Сколько метров полосы от origin по direction свободно для тела
        /// моба, но не больше max. wall — полосу обрезала стена. Без карты —
        /// открытое поле.
        /// </summary>
        private Fix64 SplitterClearLane(int id, FixVec2 origin, FixVec2 direction, Fix64 max, out bool wall)
        {
            wall = false;
            if (_layout == null || max.Raw <= 0) return max;
            Fix64 radius = Entities.BodyRadius[id];
            var previous = origin;
            Fix64 travelled = Fix64.Zero;
            while (travelled < max)
            {
                travelled = Fix64.Min(travelled + SplitterRollProbeStep, max);
                var candidate = origin + direction * travelled;
                if (!_layout.CanTravel(previous, candidate, radius))
                {
                    wall = true;
                    var low = previous; var high = candidate;
                    for (int j = 0; j < 10; j++)
                    {
                        var middle = (low + high) * Fix64.Ratio(1, 2);
                        if (_layout.CanTravel(low, middle, radius)) low = middle; else high = middle;
                    }
                    return FixVec2.Dot(low - origin, direction);
                }
                previous = candidate;
            }
            return max;
        }

        /// <summary>Полоса переката — одна фигура на метку и на проверку попадания.</summary>
        public static EnemyTelegraph SplitterRollLane(FixVec2 origin, FixVec2 direction, Fix64 length)
            => EnemyTelegraph.Lane(origin, direction, length, SplitterRollLaneWidth);

        /// <summary>Урон переката: 18/12 урона листа — глубина и «Сложно» приходят сами.</summary>
        public int SplitterRollDamageOf(int id)
            => EnemyArchetypes.Share(Entities.Damage[id], SplitterRollDamage, EnemyArchetypes.SplitterDamage);

        /// <summary>Фаза переката на текущий тик: до фиксации и пуска — по тикам, дальше — по остановке.</summary>
        private SplitterRollPhase SplitterRollPhaseOf(in SplitterState s)
        {
            if (s.RollSerial == 0) return SplitterRollPhase.None;
            if (!s.RollStopped)
            {
                if (!s.RollLocked) return SplitterRollPhase.Curl;
                return Tick < s.RollLaunchTick ? SplitterRollPhase.Locked : SplitterRollPhase.Rolling;
            }
            if (Tick >= s.RollEndTick) return SplitterRollPhase.None;
            return s.RollWallStop ? SplitterRollPhase.Dizzy : SplitterRollPhase.Uncurl;
        }

        /// <summary>Перекат Расщепеня id для вида; false — переката нет (или он уже кончился).</summary>
        public bool TryGetSplitterRoll(int id, out SplitterRollState roll)
        {
            roll = default;
            if ((uint)id >= (uint)Entities.Count) return false;
            var s = _splitters[id];
            var phase = SplitterRollPhaseOf(in s);
            if (phase == SplitterRollPhase.None) return false;
            roll = new SplitterRollState(s.RollSerial, phase, s.RollStartTick, s.RollLockTick, s.RollLaunchTick,
                s.RollStopTick, s.RollEndTick, s.RollOrigin, s.RollDirection, s.RollLength, s.RollTravelled,
                s.RollHitResolved, s.RollWallStop || s.RollWallAhead, s.RollTelegraphSerial);
            return true;
        }

        // ---- перекат клубком: точки подключения к общему коду ----

        /// <summary>Перекат готов и ещё не начат: Расщепень держит кольцо переката, а не укуса.</summary>
        internal bool SplitterWantsRoll(int id)
            => (uint)id < (uint)Entities.Count && Entities.Kind[id] == EnemyKind.ForestSplitter
               && _splitters[id].RollArmed && _splitters[id].RollSerial == 0 && Tick >= _splitters[id].RollReadyTick;

        /// <summary>Занят ли Расщепень перекатом (сжатие, пуск, раскрытие, оглушение о стену): общий замах не бьёт.</summary>
        private bool SplitterBusy(int id)
            => (uint)id < (uint)Entities.Count && _splitters[id].RollSerial != 0;

        /// <summary>Катится ли клубок: пока катится, расталкивание его не двигает (как таран Камнекопыта).</summary>
        private bool SplitterOwnsPosition(int id)
            => (uint)id < (uint)Entities.Count && SplitterRollPhaseOf(in _splitters[id]) == SplitterRollPhase.Rolling;

        /// <summary>Держит ли перекат ближний жетон: от сжатия до остановки.</summary>
        internal bool SplitterRollHoldsMeleeToken(int id)
            => (uint)id < (uint)Entities.Count && _splitters[id].RollSerial != 0 && !_splitters[id].RollStopped;

        /// <summary>Вес переката в бюджете крупных меток (1 от сжатия до остановки), начало и тик пуска.</summary>
        private int SplitterRollMarkWeight(int id, out int start, out int impact)
        {
            start = int.MinValue; impact = int.MinValue;
            if (!SplitterRollHoldsMeleeToken(id)) return 0;
            start = _splitters[id].RollStartTick; impact = _splitters[id].RollLaunchTick;
            return 1;
        }

        /// <summary>
        /// Ставит умершего Расщепеня в очередь распада. Зовёт Kill — и только
        /// для вида ForestSplitter. Повтор того же индекса за тик не ставится.
        /// </summary>
        private void QueueSplit(int id)
        {
            if ((uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestSplitter) return;
            for (int k = 0; k < _pendingSplitCount; k++) if (_pendingSplits[k].Parent == id) return;
            if (_pendingSplitCount < _pendingSplits.Length)
                _pendingSplits[_pendingSplitCount++] = new PendingSplitterSplit(id, Tick + SplitterDeathReleaseTicks,
                    Entities.MaxHealth[id], Entities.Damage[id], Entities.Position[id], Entities.Facing[id]);
        }

        /// <summary>
        /// Распад тех, чья трещина дошла до кадра 12, в порядке смерти.
        /// Зовётся после TickIgnite и до UpdateEncounterWaves; до выпуска
        /// очередь сохраняется между шагами, дети ещё не существуют.
        ///
        /// Не внутри Kill: тот зовётся посреди обходов сущностей, и новые
        /// индексы в середине обхода получили бы ход, удар или урон в тик
        /// рождения. Здесь все обходы тика уже прошли, и сетка пересобирается
        /// один раз на всех детей.
        /// </summary>
        private void ResolvePendingSplits()
        {
            if (_pendingSplitCount == 0) return;
            int kept = 0;
            bool spawned = false;
            for (int k = 0; k < _pendingSplitCount; k++)
            {
                var pending = _pendingSplits[k];
                if (Tick < pending.ReleaseTick) { _pendingSplits[kept++] = pending; continue; }
                // EntityStore.Spawn ёмкость не проверяет. Встречи считают места
                // на детей заранее (EnemyArchetypes.BodiesPerSpawn), так что
                // полный пул — только стенд; там детей просто нет.
                if (Entities.Count + SplitChildren > Entities.Capacity) continue;
                SplitParent(pending);
                spawned = true;
            }
            Array.Clear(_pendingSplits, kept, _pendingSplitCount - kept);
            _pendingSplitCount = kept;
            if (spawned) Grid.Rebuild(Entities);
        }

        /// <summary>
        /// Дети одного родителя: встают подряд по индексу, первый — влево от
        /// взгляда родителя, второй — вправо, в SplitSideOffset от его центра.
        /// Числа — доля родителя: мёртвый держит свои MaxHealth и Damage, а в
        /// них уже вошли глубина арены, «Сложно» и подстройка пачки.
        /// </summary>
        private void SplitParent(in PendingSplitterSplit pending)
        {
            int parent = pending.Parent;
            FixVec2 at = pending.Position;
            FixVec2 facing = pending.Facing;
            FixVec2 side = new FixVec2(-facing.Y, facing.X).Normalized();
            if (side.LengthSq.Raw == 0) side = new FixVec2(Fix64.Zero, Fix64.One);

            int health = Math.Max(1, EnemyArchetypes.Share(pending.Health,
                EnemyArchetypes.SplitlingHealth, EnemyArchetypes.SplitterHealth));
            Fix64 damage = Fix64.FromInt(EnemyArchetypes.Share(pending.Damage,
                EnemyArchetypes.SplitlingDamage, EnemyArchetypes.SplitterDamage));
            Fix64 radius = EnemyArchetypes.SplitlingBodyRadius;
            FixVec2 hero = Entities.Position[PlayerId];

            int first = Entities.Count;
            _events.Add(SimEvent.Split(parent, first, SplitChildren, at));
            for (int n = 0; n < SplitChildren; n++)
            {
                FixVec2 away = (n & 1) == 0 ? side : -side;
                FixVec2 spot = at + away * SplitSideOffset;
                // Родитель стоял на проходимом, и тело детёныша в 0,1 м от его
                // центра в нём помещается. Если родителя всё же вытолкнули в
                // стену — встаём ровно в его центр, а не в стену.
                if (_layout != null && !_layout.IsWalkable(spot, radius)) spot = at;

                int child = Entities.Spawn(spot, health, Faction.Orvill);
                ConfigureEnemy(child, EnemyKind.ForestSplitling);
                Entities.Stats[child].SetBase(StatType.Damage, damage);
                Entities.RefreshStats(child);
                Entities.Health[child] = Entities.MaxHealth[child];
                _splitters[child].Parent = parent;
                if (_eliteMask != null && child < _eliteMask.Length) _eliteMask[child] = false;

                // Герой уже в бою с родителем — дети не ждут обнаружения.
                Entities.Aggro[child] = true;
                FixVec2 look = (hero - spot).Normalized();
                Entities.Facing[child] = look.LengthSq.Raw != 0 ? look : facing;

                // Тик рождения уже идёт; новый замах допускается только
                // при возрасте 15 тиков от настоящего появления.
                Entities.NextAttackTick[child] = Tick + SplitlingSpawnGuardTicks;

                // Выброс прямой и упирается в стену (ResolveForcedMotion): тело
                // не скользит вдоль ствола и не пролетает сквозь него.
                ForcedMotion.Begin(Entities, child, spot + away * SplitPopDistance,
                    SplitPopTicks, ForcedMotionKind.SplitPop);
                _events.Add(SimEvent.Spawn(child, spot));
            }
        }

        private void HashSplitters(ref ulong hash)
        {
            bool present = _pendingSplitCount != 0 || _splitterRollSerial != 0;
            for (int id = 1; id < Entities.Count && !present; id++)
                present = Entities.Kind[id] == EnemyKind.ForestSplitter || Entities.Kind[id] == EnemyKind.ForestSplitling;
            if (!present) return;
            Hashing.Mix(ref hash, 0x53504C54); Hashing.Mix(ref hash, _pendingSplitCount);
            for (int k = 0; k < _pendingSplitCount; k++)
            {
                var p = _pendingSplits[k];
                Hashing.Mix(ref hash, p.Parent); Hashing.Mix(ref hash, p.ReleaseTick);
                Hashing.Mix(ref hash, p.Health); Hashing.Mix(ref hash, p.Damage);
                Hashing.Mix(ref hash, p.Position.X); Hashing.Mix(ref hash, p.Position.Y);
                Hashing.Mix(ref hash, p.Facing.X); Hashing.Mix(ref hash, p.Facing.Y);
            }
            Hashing.Mix(ref hash, _splitterRollSerial);
            for (int id = 1; id < Entities.Count; id++)
            {
                var kind = Entities.Kind[id];
                if (kind != EnemyKind.ForestSplitter && kind != EnemyKind.ForestSplitling) continue;
                var s = _splitters[id];
                Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, s.Parent);
                Hashing.Mix(ref hash, s.RollArmed ? 1 : 0); Hashing.Mix(ref hash, s.RollReadyTick);
                Hashing.Mix(ref hash, s.RollSerial);
                if (s.RollSerial == 0) continue;
                Hashing.Mix(ref hash, s.RollStartTick); Hashing.Mix(ref hash, s.RollLockTick);
                Hashing.Mix(ref hash, s.RollLaunchTick); Hashing.Mix(ref hash, s.RollStopTick);
                Hashing.Mix(ref hash, s.RollEndTick); Hashing.Mix(ref hash, s.RollTicks);
                Hashing.Mix(ref hash, s.RollTelegraphSerial);
                Hashing.Mix(ref hash, s.RollOrigin.X); Hashing.Mix(ref hash, s.RollOrigin.Y);
                Hashing.Mix(ref hash, s.RollDirection.X); Hashing.Mix(ref hash, s.RollDirection.Y);
                Hashing.Mix(ref hash, s.RollPrevious.X); Hashing.Mix(ref hash, s.RollPrevious.Y);
                Hashing.Mix(ref hash, s.RollLength); Hashing.Mix(ref hash, s.RollTravelled);
                Hashing.Mix(ref hash, (s.RollLocked ? 1 : 0) | (s.RollStopped ? 2 : 0) | (s.RollWallAhead ? 4 : 0)
                    | (s.RollBlocked ? 8 : 0) | (s.RollWallStop ? 16 : 0) | (s.RollHitResolved ? 32 : 0));
            }
        }
    }
}
