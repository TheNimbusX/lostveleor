using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Чем ведётся тело моба на экране (ревью владельца 01.10, «лунная походка»).</summary>
    public enum EnemyBodyPolicy : byte
    {
        /// <summary>Тело = взгляд Sim, как было всегда: Вендиго, Камнекопыт, Шипомёт, Корнехват.</summary>
        SimFacing = 0,

        /// <summary>Идёт сам — тело по ходу; в действии и почти стоя — по взгляду Sim.</summary>
        Travel = 1,

        /// <summary>Как Travel, но отход назад — лицом к герою обратным шагом (Плюй-плод).</summary>
        TravelKeepBackpedal = 2,
    }

    /// <summary>Что сейчас ведёт тело — для записи съёмки и тестов.</summary>
    public enum EnemyBodyMode : byte
    {
        /// <summary>Взгляд Sim: стоит, медленно переступает или политика «как Sim».</summary>
        Sim = 0,

        /// <summary>Обязательство (замах, перекат, залп, оглушение, волок, выход из земли): быстро к взгляду Sim.</summary>
        Commit = 1,

        /// <summary>Идёт сам: тело по ходу.</summary>
        Travel = 2,

        /// <summary>Пятится: лицом к герою, ноги назад.</summary>
        Backpedal = 3,
    }

    /// <summary>Защёлки правила на одно тело: гистерезис хода и отхода назад.</summary>
    [Flags]
    public enum EnemyBodyLatch : byte
    {
        None = 0,
        Travel = 1,
        Backpedal = 2,
    }

    /// <summary>Куда и как быстро доворачивать тело в этом кадре.</summary>
    public readonly struct EnemyBodyTarget
    {
        /// <summary>Направление цели в осях Sim (X, Y) = мир (x, z), единичное.</summary>
        public readonly float X, Y;

        /// <summary>Резкость экспоненциального догона, 1/с: доля пути за кадр = 1 − exp(−резкость·dt).</summary>
        public readonly float Sharpness;

        /// <summary>Потолок скорости поворота, градусы в секунду; бесконечность — без потолка.</summary>
        public readonly float MaxDegreesPerSecond;

        /// <summary>Тело дальше этого угла от цели доворачивает рывком (PivotDegreesPerSecond); бесконечность — без рывка.</summary>
        public readonly float PivotGapDegrees;

        /// <summary>Потолок поворота рывком, градусы в секунду.</summary>
        public readonly float PivotDegreesPerSecond;

        public readonly EnemyBodyMode Mode;

        public EnemyBodyTarget(float x, float y, float sharpness, float maxDegreesPerSecond, EnemyBodyMode mode,
            float pivotGapDegrees = float.PositiveInfinity, float pivotDegreesPerSecond = 0f)
        {
            X = x; Y = y; Sharpness = sharpness; MaxDegreesPerSecond = maxDegreesPerSecond; Mode = mode;
            PivotGapDegrees = pivotGapDegrees; PivotDegreesPerSecond = pivotDegreesPerSecond;
        }
    }

    /// <summary>
    /// НАПРАВЛЕНИЕ ТЕЛА МОБА НА ЭКРАНЕ — ТОЛЬКО ВИД (ревью владельца 01.10: «бегут вбок, а
    /// анимация бега вперёд»). Разбор — ART/characters/act-1-enemies/review/sidestep-fix-plan.md,
    /// вариант (а).
    ///
    /// В симуляции у ближника два независимых решения: куда смотреть (почти всегда на героя)
    /// и куда идти (к своему месту вокруг героя, по дуге). Когда герой бежит вдоль толпы, моб
    /// едет боком под 40–90° к взгляду, а боковых клипов нет ни у кого — ноги «бегут вперёд».
    /// Взгляд Sim — боевой: по нему стартует замах (±37° у Хранителя), катится Расщепень,
    /// стреляет Плюй-плод. Поэтому Sim не трогаем, а развязываем тело:
    ///
    /// 1. обязательство (замах и восстановление, перекат, залп, оглушение, волок или выпад,
    ///    выход из-под земли) — тело за ~0,1 с к взгляду Sim (900°/с): каждый удар начинается
    ///    из боевого направления, метка и знак удара на теле совпадают с телом;
    /// 2. идёт сам — шаг ≥ 30% своего (выход ниже 20%, гистерезис) — тело по ходу: 540°/с,
    ///    быстрее разворота Sim 360°/с, а если ход ушёл от тела дальше 45° — рывком 1440°/с:
    ///    боком больше 45° тело не едет;
    /// 3. почти стоит — взгляд Sim, как раньше;
    /// 4. Плюй-плод при отходе назад (ход дальше 120° от взгляда, выход ближе 105°) — спиной
    ///    по ходу, то есть почти лицом к герою, ноги назад (ForestBudAnimatorView).
    ///
    /// ПОЧЕМУ НЕ 45/30% И НЕ ТОЛЬКО 540°/с, КАК В ПЛАНЕ. Замер на толпе CrowdStepHashPinTests
    /// (EnemyBodyFacingTests): с числами плана «лунная походка» падала с 20–23% кадров хода только
    /// до 9–11%. Почти вся остальная — первые кадры хода: моб стоит лицом к герою и трогается к
    /// своему месту под 90–180° (Approach набирает шаг за три тика: 33 → 67 → 100%). При пороге
    /// 45% тело начинало поворот, когда моб уже шёл на 67% шага. С порогом 30% поворот начинается
    /// на первом же тике хода, а рывок дальше 45° закрывает разворот за 0,06–0,12 с.
    /// Итог: 1,4–1,8% (круг, бой, проходы мимо толпы); удары — тело в 3° от направления удара.
    ///
    /// Вендиго, Камнекопыт, Шипомёт, Корнехват — «как Sim»: они и так ходят лицом вперёд, а по
    /// их телу игрок читает опасность («за спиной Вендиго», «перед клыками кабана»).
    ///
    /// Без UnityEngine — правило проверяют тесты вне Unity
    /// (tools/Combat.Presentation.Tests/EnemyBodyFacingTests.cs), в том числе на толпе
    /// CrowdStepHashPinTests. Направления — в осях Sim (X, Y), в мире это (x, z).
    /// </summary>
    public static class EnemyBodyFacingRules
    {
        /// <summary>
        /// Тело идёт по ходу, когда шаг не меньше этой доли своего полного шага: первый тик хода
        /// (Approach — треть шага за тик) уже поворачивает тело.
        /// </summary>
        public const float TravelEnterShare = .30f;

        /// <summary>…и перестаёт, когда шаг упал ниже этой доли: между 20 и 30% решение не мигает.</summary>
        public const float TravelExitShare = .20f;

        /// <summary>Поворот тела по ходу, °/с. Разворот Sim — 12° за тик = 360°/с.</summary>
        public const float TravelTurnDegreesPerSecond = 540f;

        /// <summary>Резкость догона хода: 30-герцовые ступени Velocity сглажены, отставание на дуге ~ω/30.</summary>
        public const float TravelSharpness = 30f;

        /// <summary>Ход ушёл от тела дальше этого угла — тело доворачивает рывком: боком дальше 45° не едет.</summary>
        public const float PivotGapDegrees = 45f;

        /// <summary>Рывок разворота на ходу: 90° за 0,06 с, разворот кругом за 0,12 с.</summary>
        public const float PivotDegreesPerSecond = 1440f;

        /// <summary>Поворот к боевому направлению в обязательстве: 90° за 0,1 с.</summary>
        public const float CommitTurnDegreesPerSecond = 900f;

        /// <summary>Отход назад начинается, когда ход дальше этого угла от взгляда Sim…</summary>
        public const float BackpedalEnterDegrees = 120f;

        /// <summary>…и кончается ближе этого: отход на 35° вбок (BudRetreatHeading) не мигает.</summary>
        public const float BackpedalExitDegrees = 105f;

        /// <summary>Резкость догона взгляда Sim — прежняя OrvillTurnSharpness: «как Sim» выглядит как раньше.</summary>
        public const float FollowSharpness = 20f;

        /// <summary>Резкость доворота в обязательстве: 90° укладываются в ~0,1 с вместе с потолком 900°/с.</summary>
        public const float CommitSharpness = 30f;

        /// <summary>Кто ходит телом по ходу. Вид вне списка — «как Sim», как было.</summary>
        public static EnemyBodyPolicy PolicyOf(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.None: // моб без вида живёт по правилам Хранителя
                case EnemyKind.ForestGuardian:
                case EnemyKind.ForestRootSwarm:
                case EnemyKind.ForestSplitter:
                case EnemyKind.ForestSplitling:
                    return EnemyBodyPolicy.Travel;
                case EnemyKind.ForestBud:
                    return EnemyBodyPolicy.TravelKeepBackpedal;
                // Хозяин Чащи идёт только вдоль взгляда (Sim, 2,5°/тик), а в действии смотрит,
                // куда бьёт: тело — всегда взгляд Sim, лунной походки у него нет.
                case EnemyKind.ForestThicketMaster:
                    return EnemyBodyPolicy.SimFacing;
                default:
                    return EnemyBodyPolicy.SimFacing;
            }
        }

        /// <summary>
        /// Моб в действии — тело обязано смотреть по взгляду Sim: замах или восстановление общего
        /// удара, перекат Расщепеня (с оглушением о стену), залп Плюй-плода, оглушение, волок,
        /// отброс или свой выпад (Velocity там устаревший), выход из-под земли. Только читает Sim.
        /// </summary>
        public static bool IsCommitted(Simulation sim, int id)
        {
            var entities = sim.Entities;
            if ((uint)id >= (uint)entities.Count) return false;
            int tick = sim.Tick;
            if (sim.TryGetEnemySwing(id, out var swing) && tick < swing.RecoverUntil) return true;
            if (sim.TryGetSplitterRoll(id, out _)) return true;
            if (sim.TryGetForestBudAttack(id, out _)) return true;
            // Любое действие Хозяина Чащи (пробуждение, рёв, лапа, топот, нырок, касты, буря).
            if (sim.TryGetThicketMasterAction(id, out _)) return true;
            if (sim.Statuses.IsStunned(id, tick)) return true;
            if (entities.ForcedTicksLeft[id] > 0) return true;
            return sim.IsEmerging(id);
        }

        /// <summary>
        /// Цель поворота тела в этом кадре. <paramref name="simX"/>, <paramref name="simY"/> —
        /// взгляд Sim (интерполированный, как у TickDriver.GetRenderFacing);
        /// <paramref name="velocityX"/>, <paramref name="velocityY"/> — собственный шаг за тик
        /// (EntityStore.Velocity: расталкивание его не меняет); <paramref name="moveStep"/> — полный
        /// шаг за тик (EntityStore.MoveStep); <paramref name="committed"/> — моб в действии.
        /// <paramref name="latch"/> — защёлки этого тела между кадрами.
        /// </summary>
        public static EnemyBodyTarget Target(EnemyBodyPolicy policy, float simX, float simY,
            float velocityX, float velocityY, float moveStep, bool committed, ref EnemyBodyLatch latch)
        {
            if (policy == EnemyBodyPolicy.SimFacing)
            {
                latch = EnemyBodyLatch.None;
                return new EnemyBodyTarget(simX, simY, FollowSharpness, float.PositiveInfinity, EnemyBodyMode.Sim);
            }
            if (committed)
            {
                latch = EnemyBodyLatch.None;
                return new EnemyBodyTarget(simX, simY, CommitSharpness, CommitTurnDegreesPerSecond, EnemyBodyMode.Commit);
            }

            float speed = (float)Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
            bool travel = (latch & EnemyBodyLatch.Travel) != 0;
            travel = moveStep > 0f && speed > 1e-6f
                     && speed >= moveStep * (travel ? TravelExitShare : TravelEnterShare);
            if (!travel)
            {
                latch = EnemyBodyLatch.None;
                return new EnemyBodyTarget(simX, simY, FollowSharpness, TravelTurnDegreesPerSecond, EnemyBodyMode.Sim);
            }

            float travelX = velocityX / speed, travelY = velocityY / speed;
            if (policy == EnemyBodyPolicy.TravelKeepBackpedal)
            {
                float away = AngleBetween(simX, simY, travelX, travelY);
                bool back = (latch & EnemyBodyLatch.Backpedal) != 0
                    ? away > BackpedalExitDegrees
                    : away > BackpedalEnterDegrees;
                if (back)
                {
                    latch = EnemyBodyLatch.Travel | EnemyBodyLatch.Backpedal;
                    // Спиной по ходу: ноги идут ровно назад, а лицо — в пределах 75° от героя.
                    return new EnemyBodyTarget(-travelX, -travelY, TravelSharpness, TravelTurnDegreesPerSecond,
                        EnemyBodyMode.Backpedal, PivotGapDegrees, PivotDegreesPerSecond);
                }
            }
            latch = EnemyBodyLatch.Travel;
            return new EnemyBodyTarget(travelX, travelY, TravelSharpness, TravelTurnDegreesPerSecond,
                EnemyBodyMode.Travel, PivotGapDegrees, PivotDegreesPerSecond);
        }

        /// <summary>
        /// Шаг тела к цели за <paramref name="dt"/> секунд: экспоненциальный догон (прячет
        /// 30-герцовые ступени хода) с потолком скорости; дальше PivotGapDegrees от цели — потолок
        /// рывка. Без потолка это ровно прежний Slerp(previous, facing, 1 − exp(−20·dt)) в
        /// плоскости земли. Нулевое тело встаёт в цель.
        /// </summary>
        public static void Step(ref float x, ref float y, in EnemyBodyTarget target, float dt)
        {
            float length = (float)Math.Sqrt(x * x + y * y);
            if (length < 1e-4f)
            {
                x = target.X; y = target.Y;
                return;
            }
            x /= length; y /= length;
            if (dt <= 0f) return;
            float delta = SignedAngle(x, y, target.X, target.Y);
            float turn = delta * (1f - (float)Math.Exp(-target.Sharpness * dt));
            float cap = (Math.Abs(delta) > target.PivotGapDegrees
                ? Math.Max(target.MaxDegreesPerSecond, target.PivotDegreesPerSecond)
                : target.MaxDegreesPerSecond) * dt;
            if (turn > cap) turn = cap;
            else if (turn < -cap) turn = -cap;
            Rotate(ref x, ref y, turn);
        }

        /// <summary>Угол от (ax, ay) к (bx, by) в градусах, −180…180, против часовой в осях Sim.</summary>
        public static float SignedAngle(float ax, float ay, float bx, float by)
            => (float)(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by) * (180.0 / Math.PI));

        /// <summary>Угол между направлениями в градусах, 0…180.</summary>
        public static float AngleBetween(float ax, float ay, float bx, float by)
            => Math.Abs(SignedAngle(ax, ay, bx, by));

        /// <summary>Поворот единичного (x, y) на <paramref name="degrees"/> против часовой в осях Sim.</summary>
        public static void Rotate(ref float x, ref float y, float degrees)
        {
            double radians = degrees * (Math.PI / 180.0);
            float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
            float rx = x * cos - y * sin, ry = x * sin + y * cos;
            float length = (float)Math.Sqrt(rx * rx + ry * ry);
            if (length > 1e-6f) { rx /= length; ry /= length; }
            x = rx; y = ry;
        }
    }

    /// <summary>
    /// ПЕРЕСТУПАНИЕ ПРИ РАЗВОРОТЕ НА МЕСТЕ (ревью владельца 01.10, Вендиго: «проворот на месте
    /// без анимации, когда мы его крутим»). Моб стоит, а корпус крутится: ноги переступают фазой
    /// клипа Walk в темпе поворота, а не едут под Idle. Та же заплатка, что у Шипомёта и
    /// Корнехвата (ThorncasterAnimatorView, RootSnarerAnimatorView), вынесенная в одно место для
    /// Вендиго, Плюй-плода и Расщепеня. Поворот — показанного тела (ArenaView.BodyFacing): тело
    /// «по ходу» доворачивается к взгляду Sim после остановки, и это тоже шаг на месте.
    /// Без UnityEngine — проверяется в EnemyBodyFacingTests.
    /// </summary>
    public struct EnemyTurnSteps
    {
        /// <summary>Медленнее этого корпус не «крутится»: дрожь сглаживания не будит ноги.</summary>
        public const float RateThreshold = 30f;

        /// <summary>Сколько держать шаг после последнего поворота: кадры без сдвига (30 Гц Sim) не мигают Idle.</summary>
        public const float HoldSeconds = .2f;

        private float _hold;

        public bool Active => _hold > 0f;

        public void Reset() => _hold = 0f;

        /// <summary>
        /// Кадр: корпус повернулся на <paramref name="yawDegrees"/> за <paramref name="dt"/> секунд.
        /// True — моб переступает: фаза <paramref name="walkPhase"/> уже сдвинута на
        /// |поворот| / <paramref name="degreesPerCycle"/> (полный цикл шага на столько градусов).
        /// </summary>
        public bool Step(float yawDegrees, float dt, float degreesPerCycle, ref float walkPhase)
        {
            if (dt <= 0f) return _hold > 0f;
            float yaw = Math.Abs(yawDegrees);
            if (yaw / dt > RateThreshold) _hold = HoldSeconds;
            else _hold -= dt;
            if (_hold <= 0f) { _hold = 0f; return false; }
            walkPhase += yaw / Math.Max(1f, degreesPerCycle);
            return true;
        }
    }
}
