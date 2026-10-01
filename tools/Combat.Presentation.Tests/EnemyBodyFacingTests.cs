using System;
using System.Collections.Generic;
using System.Text;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// «Лунная походка» мобов (ревью владельца 01.10: «бегут вбок, при этом анимация бега вперёд,
/// если мы бежим от них вдоль»). Правило — razlom/Assets/Game.View/EnemyBodyFacingRules.cs,
/// разбор — ART/characters/act-1-enemies/review/sidestep-fix-plan.md, вариант (а): тело моба
/// по ходу, пока он идёт сам; в действии — за ~0,1 с к взгляду Sim. Только вид: бой не меняется.
///
/// Единичные случаи правила и сцена толпы CrowdStepHashPinTests (лесная арена 6, сид 42,
/// 48 мобов) в трёх прогонах героя: круг, бой, проходы вдоль толпы. Сим крутится 1800 тиков,
/// правило — на кадрах 60 Гц с интерполяцией взгляда, как в TickDriver.GetRenderFacing.
/// Меряется доля кадров «лунной походки» до (тело = взгляд Sim, как было) и после, угол тела к
/// удару в тик контакта и то, что StateHash с правилом и без него совпадает по тикам (правило
/// только читает симуляцию). Числа хэша здесь не прибиты: их держит CrowdStepHashPinTests, и он
/// обязан остаться зелёным без переснятия. Тест печатает все числа «до → после» по видам.
/// </summary>
public sealed class EnemyBodyFacingTests
{
    private const float Frame = 1f / 60f;

    // ---------- правило ----------

    [Test]
    public void SideTravel_TurnsTheBodyAlongTheWalkWithinATenthOfASecond()
    {
        // Взгляд Sim на герое (+Y), шаг полный вбок (+X).
        float reached = TimeToReach(EnemyBodyPolicy.Travel, 0f, 1f, .1f, 0f, false, 1f, 0f, 10f, out var mode);
        Assert.That(mode, Is.EqualTo(EnemyBodyMode.Travel));
        Assert.That(reached, Is.GreaterThan(0f).And.LessThanOrEqualTo(.12f), "тело дошло до хода за " + reached + " с");
    }

    [Test]
    public void ReversedWalk_PivotsFast_ButSmallBendsFollowAt540()
    {
        // Разворот хода кругом: рывок 1440°/с, а не 540°/с.
        float bx = 0f, by = 1f;
        var latch = EnemyBodyLatch.None;
        var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, 0f, -.1f, .1f, false, ref latch);
        EnemyBodyFacingRules.Step(ref bx, ref by, target, Frame);
        Assert.That(EnemyBodyFacingRules.AngleBetween(0f, 1f, bx, by),
            Is.EqualTo(EnemyBodyFacingRules.PivotDegreesPerSecond * Frame).Within(.01f));
        float reached = TimeToReach(EnemyBodyPolicy.Travel, 0f, 1f, 0f, -.1f, false, 0f, -1f, 45f, out _);
        Assert.That(reached, Is.LessThanOrEqualTo(.12f), "кругом до 45° от хода за " + reached + " с");

        // Изгиб дуги на 30° — плавно, потолок 540°/с.
        float cx = 0f, cy = 1f;
        float sin30 = .5f, cos30 = (float)Math.Sqrt(.75);
        latch = EnemyBodyLatch.None;
        target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, sin30 * .1f, cos30 * .1f, .1f, false, ref latch);
        EnemyBodyFacingRules.Step(ref cx, ref cy, target, Frame);
        Assert.That(EnemyBodyFacingRules.AngleBetween(0f, 1f, cx, cy),
            Is.EqualTo(EnemyBodyFacingRules.TravelTurnDegreesPerSecond * Frame).Within(.01f));
    }

    [Test]
    public void SwingRollVolley_ReturnTheBodyToTheSimFacingWithinTwelveHundredths()
    {
        foreach (var policy in new[] { EnemyBodyPolicy.Travel, EnemyBodyPolicy.TravelKeepBackpedal })
        {
            // Тело шло по ходу (+X), Sim смотрит на героя (+Y), начался замах: шаг 0.
            float reached = TimeToReach(policy, 0f, 1f, 0f, 0f, true, 0f, 1f, 10f, out var mode, 1f, 0f);
            Assert.That(mode, Is.EqualTo(EnemyBodyMode.Commit), policy.ToString());
            Assert.That(reached, Is.GreaterThan(0f).And.LessThanOrEqualTo(.12f), policy + ": к Sim за " + reached + " с");
        }
        var latch = EnemyBodyLatch.Travel;
        EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, 0f, 0f, .1f, true, ref latch);
        Assert.That(latch, Is.EqualTo(EnemyBodyLatch.None), "обязательство сбрасывает защёлку хода");
    }

    [Test]
    public void CommittedWhileStillMoving_StillFacesTheSim()
    {
        // Перекат: шаг вдоль своей полосы; волок: Velocity устаревший — тело всё равно по Sim.
        var latch = EnemyBodyLatch.Travel;
        var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, .1f, 0f, .1f, true, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Commit));
        Assert.That(target.X, Is.EqualTo(0f));
        Assert.That(target.Y, Is.EqualTo(1f));
        Assert.That(target.MaxDegreesPerSecond, Is.EqualTo(EnemyBodyFacingRules.CommitTurnDegreesPerSecond));
    }

    [Test]
    public void SlowStep_KeepsTheSimFacing()
    {
        var latch = EnemyBodyLatch.None;
        var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, .029f, 0f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Sim), "29% с места — ещё стоит");
        Assert.That(target.Y, Is.EqualTo(1f));
        // Из защёлки хода: ниже 20% — стоит.
        latch = EnemyBodyLatch.Travel;
        target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, .019f, 0f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Sim));
        Assert.That(latch, Is.EqualTo(EnemyBodyLatch.None));
        // Стоит — без шага вовсе.
        target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, 0f, 0f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Sim));
    }

    [Test]
    public void Hysteresis_DoesNotFlickerAroundAQuarterStep()
    {
        var latch = EnemyBodyLatch.None;
        // Переступание с места на 23–27% — тело по Sim.
        for (int f = 0; f < 10; f++)
        {
            float share = f % 2 == 0 ? .23f : .27f;
            var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, .1f * share, 0f, .1f, false, ref latch);
            Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Sim), "кадр " + f);
        }
        // Пошёл полным шагом, потом сбросил до 23–27% — остаётся по ходу.
        EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, .1f, 0f, .1f, false, ref latch);
        for (int f = 0; f < 10; f++)
        {
            float share = f % 2 == 0 ? .23f : .27f;
            var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, .1f * share, 0f, .1f, false, ref latch);
            Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Travel), "кадр " + f);
        }
    }

    [Test]
    public void Bud_BackpedalsFacingTheHero_AndTurnsSideways()
    {
        var latch = EnemyBodyLatch.None;
        // Отход назад на 35° от прямой: 145° от взгляда — спиной по ходу, почти лицом к герою.
        float rx = (float)Math.Sin(35.0 * Math.PI / 180.0), ry = -(float)Math.Cos(35.0 * Math.PI / 180.0);
        var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.TravelKeepBackpedal, 0f, 1f, rx * .1f, ry * .1f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Backpedal));
        Assert.That(target.X, Is.EqualTo(-rx).Within(1e-5f));
        Assert.That(target.Y, Is.EqualTo(-ry).Within(1e-5f));
        Assert.That(EnemyBodyFacingRules.AngleBetween(target.X, target.Y, 0f, 1f), Is.LessThan(40f), "лицо к герою");
        // 110° — внутри гистерезиса: отход продолжается.
        float sx = (float)Math.Sin(110.0 * Math.PI / 180.0), sy = (float)Math.Cos(110.0 * Math.PI / 180.0);
        target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.TravelKeepBackpedal, 0f, 1f, sx * .1f, sy * .1f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Backpedal));
        // Те же 110° с нуля — это ход вбок, тело по ходу.
        latch = EnemyBodyLatch.None;
        target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.TravelKeepBackpedal, 0f, 1f, sx * .1f, sy * .1f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Travel));
        // Смена огневой точки вбок полным шагом — тело по ходу.
        latch = EnemyBodyLatch.None;
        target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.TravelKeepBackpedal, 0f, 1f, .1f, 0f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Travel));
        Assert.That(target.X, Is.EqualTo(1f).Within(1e-6f));
        // Те, у кого шага назад нет, назад разворачиваются по ходу.
        latch = EnemyBodyLatch.None;
        target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.Travel, 0f, 1f, rx * .1f, ry * .1f, .1f, false, ref latch);
        Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Travel));
    }

    [Test]
    public void ReadByTheirBodies_KeepTheSimFacingAlways()
    {
        foreach (var kind in new[] { EnemyKind.ForestWendigo, EnemyKind.ForestStonehoof, EnemyKind.ForestThorncaster, EnemyKind.ForestRootSnarer })
        {
            var policy = EnemyBodyFacingRules.PolicyOf(kind);
            Assert.That(policy, Is.EqualTo(EnemyBodyPolicy.SimFacing), kind.ToString());
            foreach (bool committed in new[] { false, true })
            {
                var latch = EnemyBodyLatch.None;
                var target = EnemyBodyFacingRules.Target(policy, 0f, 1f, .1f, 0f, .1f, committed, ref latch);
                Assert.That(target.Mode, Is.EqualTo(EnemyBodyMode.Sim), kind + " в действии " + committed);
                Assert.That(target.X, Is.EqualTo(0f));
                Assert.That(target.Sharpness, Is.EqualTo(20f), "прежняя OrvillTurnSharpness");
                Assert.That(float.IsPositiveInfinity(target.MaxDegreesPerSecond), Is.True, "без потолка, как было");
                Assert.That(float.IsPositiveInfinity(target.PivotGapDegrees), Is.True, "без рывка");
            }
        }
        foreach (var kind in new[] { EnemyKind.ForestGuardian, EnemyKind.ForestRootSwarm, EnemyKind.ForestSplitter, EnemyKind.ForestSplitling, EnemyKind.None })
            Assert.That(EnemyBodyFacingRules.PolicyOf(kind), Is.EqualTo(EnemyBodyPolicy.Travel), kind.ToString());
        Assert.That(EnemyBodyFacingRules.PolicyOf(EnemyKind.ForestBud), Is.EqualTo(EnemyBodyPolicy.TravelKeepBackpedal));
    }

    [Test]
    public void SimFacingStep_IsThePreviousSlerpInTheGroundPlane()
    {
        // Прежний ArenaView: Slerp(previous, facing, 1 − exp(−20·dt)) — поворот на ту же долю угла.
        foreach (float degrees in new[] { 10f, 90f, 170f })
        {
            float bx = (float)Math.Sin(degrees * Math.PI / 180.0), by = (float)Math.Cos(degrees * Math.PI / 180.0);
            var latch = EnemyBodyLatch.None;
            var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.SimFacing, 0f, 1f, 0f, 0f, .1f, false, ref latch);
            EnemyBodyFacingRules.Step(ref bx, ref by, target, Frame);
            float expected = degrees * (float)Math.Exp(-20f * Frame);
            Assert.That(EnemyBodyFacingRules.AngleBetween(0f, 1f, bx, by), Is.EqualTo(expected).Within(1e-3f), degrees + "°");
        }
    }

    [Test]
    public void TurnInPlace_StepsWhileTheBodyTurns_AndSettlesAfterAFifthOfASecond()
    {
        // Вендиго крутится за героем 180°/с — 3° за кадр: лапы идут фазой Walk, цикл на 240°.
        var steps = new EnemyTurnSteps();
        float phase = 0f;
        for (int f = 0; f < 60; f++)
            Assert.That(steps.Step(3f, Frame, 240f, ref phase), Is.True, "кадр " + f);
        Assert.That(phase, Is.EqualTo(180f / 240f).Within(1e-4f), "полсекунды разворота — три четверти цикла шага");
        // Кадры без сдвига (поворот Sim 30 Гц) шаг держат, после ~0,2 с покоя — Idle.
        int still = 0;
        while (steps.Step(0f, Frame, 240f, ref phase) && still < 100) still++;
        Assert.That(still * Frame, Is.InRange(EnemyTurnSteps.HoldSeconds - 2 * Frame, EnemyTurnSteps.HoldSeconds + Frame));
        Assert.That(phase, Is.EqualTo(180f / 240f).Within(1e-4f), "стоя фаза не идёт");
        // Дрожь сглаживания (0,2° за кадр = 12°/с) ноги не будит.
        steps.Reset();
        Assert.That(steps.Step(.2f, Frame, 240f, ref phase), Is.False);
    }

    /// <summary>Сколько секунд тело идёт до цели (в пределах <paramref name="within"/>°); −1 — не дошло за 0,5 с.</summary>
    private static float TimeToReach(EnemyBodyPolicy policy, float simX, float simY, float vx, float vy, bool committed,
        float goalX, float goalY, float within, out EnemyBodyMode mode, float bodyX = float.NaN, float bodyY = float.NaN)
    {
        float bx = float.IsNaN(bodyX) ? simX : bodyX, by = float.IsNaN(bodyY) ? simY : bodyY;
        var latch = committed ? EnemyBodyLatch.Travel : EnemyBodyLatch.None;
        mode = EnemyBodyMode.Sim;
        for (int f = 1; f <= 30; f++)
        {
            var target = EnemyBodyFacingRules.Target(policy, simX, simY, vx, vy, .1f, committed, ref latch);
            mode = target.Mode;
            EnemyBodyFacingRules.Step(ref bx, ref by, target, Frame);
            if (EnemyBodyFacingRules.AngleBetween(bx, by, goalX, goalY) <= within) return f * Frame;
        }
        return -1f;
    }

    // ---------- толпа ----------

    [Test]
    public void CircleCrowd_NoMoonwalk_AndEverySwingStartsFromTheSimFacing() => CheckCrowd(Hero.Circle);

    [Test]
    public void FightingCrowd_NoMoonwalk_AndEverySwingStartsFromTheSimFacing() => CheckCrowd(Hero.Fight);

    [Test]
    public void PassingAlongTheCrowd_NoMoonwalk_AndEverySwingStartsFromTheSimFacing() => CheckCrowd(Hero.Pass);

    /// <summary>Доля «лунной походки» после правила — не больше 3% кадров собственного хода.</summary>
    private const float MoonwalkLimit = .03f;

    /// <summary>Тело в тик контакта — не дальше 15° от направления удара.</summary>
    private const float ImpactLimitDegrees = 15f;

    /// <summary>Мерка плана: моб «идёт», когда его шаг не меньше 45% полного…</summary>
    private const float WalkingShare = .45f;

    /// <summary>…и «едет боком», когда тело расходится с ходом больше чем на 45°.</summary>
    private const float MoonwalkDegrees = 45f;

    private const int CrowdTicks = 1800;

    private static void CheckCrowd(Hero hero)
    {
        var plain = new Scene(hero);
        var watched = new Scene(hero);
        var after = new Bodies(watched.Sim, useRule: true);
        var before = new Bodies(watched.Sim, useRule: false);
        var impacts = new ImpactLog();

        for (int t = 0; t < CrowdTicks; t++)
        {
            plain.Step(t);
            before.Capture();
            after.Capture();
            watched.Step(t);
            Assert.That(watched.Sim.StateHash(), Is.EqualTo(plain.Sim.StateHash()),
                "правило вида изменило симуляцию на тике " + t);
            for (int f = 1; f <= 2; f++)
            {
                float alpha = f * .5f;
                before.Frame(alpha);
                after.Frame(alpha);
                impacts.Frame(watched.Sim, after, before);
            }
        }

        var report = new StringBuilder(hero + ": «лунная походка» до → после (кадры своего хода ≥ 45% шага, тело дальше 45° от хода):\n");
        before.Report(report, "до");
        after.Report(report, "после");
        impacts.Report(report);
        TestContext.WriteLine(report.ToString());

        Assert.That(after.TravelFrames, Is.GreaterThan(1000), "сцена должна гонять мобов: " + report);
        Assert.That(after.MoonwalkShare, Is.LessThanOrEqualTo(MoonwalkLimit), report.ToString());
        Assert.That(impacts.Count, Is.GreaterThan(0), "в сцене должны быть удары: " + report);
        Assert.That(impacts.WorstAfter, Is.LessThanOrEqualTo(ImpactLimitDegrees), report.ToString());
    }

    private enum Hero { Circle, Fight, Pass }

    /// <summary>Тела всех мобов на экране: правило или прежнее «тело = взгляд Sim».</summary>
    private sealed class Bodies
    {
        private readonly Simulation _sim;
        private readonly bool _useRule;
        private float[] _x = new float[0], _y = new float[0];
        private EnemyBodyLatch[] _latch = new EnemyBodyLatch[0];
        private bool[] _known = new bool[0];
        private FixVec2[] _previousFacing = new FixVec2[0];
        private readonly int[] _travelByKind = new int[16], _moonwalkByKind = new int[16];

        public int TravelFrames, MoonwalkFrames;
        public float MoonwalkShare => TravelFrames > 0 ? (float)MoonwalkFrames / TravelFrames : 0f;

        public Bodies(Simulation sim, bool useRule) { _sim = sim; _useRule = useRule; }

        public float AngleTo(int id, float x, float y)
            => (uint)id < (uint)_x.Length && _known[id] ? EnemyBodyFacingRules.AngleBetween(_x[id], _y[id], x, y) : 0f;

        /// <summary>Взгляд до шага: TickDriver держит его для интерполяции (_prevFacings).</summary>
        public void Capture()
        {
            var e = _sim.Entities;
            Grow(e.Count);
            for (int i = 1; i < e.Count; i++) _previousFacing[i] = e.Facing[i];
        }

        public void Frame(float alpha)
        {
            var e = _sim.Entities;
            Grow(e.Count);
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                RenderFacing(i, alpha, out float sx, out float sy);
                if (!_known[i])
                {
                    _known[i] = true; _x[i] = sx; _y[i] = sy; _latch[i] = EnemyBodyLatch.None;
                }
                var policy = _useRule ? EnemyBodyFacingRules.PolicyOf(e.Kind[i]) : EnemyBodyPolicy.SimFacing;
                float vx = e.Velocity[i].X.ToFloat(), vy = e.Velocity[i].Y.ToFloat();
                float step = e.MoveStep[i].ToFloat();
                bool committed = EnemyBodyFacingRules.IsCommitted(_sim, i);
                var target = EnemyBodyFacingRules.Target(policy, sx, sy, vx, vy, step, committed, ref _latch[i]);
                EnemyBodyFacingRules.Step(ref _x[i], ref _y[i], target, EnemyBodyFacingTests.Frame);

                // Мерка: только тела «по ходу», только свой шаг (волок и отброс — не ход).
                if (EnemyBodyFacingRules.PolicyOf(e.Kind[i]) == EnemyBodyPolicy.SimFacing) continue;
                if (e.ForcedTicksLeft[i] > 0) continue;
                float speed = (float)Math.Sqrt(vx * vx + vy * vy);
                if (step <= 0f || speed < step * WalkingShare) continue;
                float off = EnemyBodyFacingRules.AngleBetween(_x[i], _y[i], vx / speed, vy / speed);
                // Плюй-плод пятится честным обратным шагом: «едет» только боком.
                bool moonwalk = e.Kind[i] == EnemyKind.ForestBud
                    ? off > MoonwalkDegrees && off < 180f - MoonwalkDegrees
                    : off > MoonwalkDegrees;
                TravelFrames++;
                _travelByKind[(int)e.Kind[i]]++;
                if (!moonwalk) continue;
                MoonwalkFrames++;
                _moonwalkByKind[(int)e.Kind[i]]++;
            }
        }

        private void RenderFacing(int i, float alpha, out float x, out float y)
        {
            var e = _sim.Entities;
            float cx = e.Facing[i].X.ToFloat(), cy = e.Facing[i].Y.ToFloat();
            float cl = (float)Math.Sqrt(cx * cx + cy * cy);
            if (cl < 1e-6f) { x = 0f; y = 1f; return; }
            cx /= cl; cy /= cl;
            float px = _previousFacing[i].X.ToFloat(), py = _previousFacing[i].Y.ToFloat();
            float pl = (float)Math.Sqrt(px * px + py * py);
            if (pl < 1e-6f) { x = cx; y = cy; return; }
            px /= pl; py /= pl;
            // Slerp в плоскости земли — поворот на долю угла, как Vector3.Slerp у TickDriver.
            x = px; y = py;
            EnemyBodyFacingRules.Rotate(ref x, ref y, EnemyBodyFacingRules.SignedAngle(px, py, cx, cy) * alpha);
        }

        private void Grow(int count)
        {
            if (_x.Length >= count) return;
            int size = Math.Max(count, _x.Length * 2);
            Array.Resize(ref _x, size); Array.Resize(ref _y, size); Array.Resize(ref _latch, size);
            Array.Resize(ref _known, size); Array.Resize(ref _previousFacing, size);
        }

        public void Report(StringBuilder b, string label)
        {
            b.Append("  ").Append(label).Append(": ").Append(Percent(MoonwalkFrames, TravelFrames))
                .Append(" (").Append(MoonwalkFrames).Append('/').Append(TravelFrames).Append(')');
            for (int k = 0; k < _travelByKind.Length; k++)
            {
                if (_travelByKind[k] == 0) continue;
                b.Append("; ").Append((EnemyKind)k).Append(' ').Append(Percent(_moonwalkByKind[k], _travelByKind[k]))
                    .Append(" из ").Append(_travelByKind[k]);
            }
            b.Append('\n');
        }
    }

    /// <summary>Угол тела к удару в тик контакта: замах, перекат, первый плод залпа.</summary>
    private sealed class ImpactLog
    {
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly int[] _byKind = new int[16];
        private readonly float[] _worstByKind = new float[16];
        public int Count;
        public float WorstAfter, WorstBefore, SumAfter;

        public void Frame(Simulation sim, Bodies after, Bodies before)
        {
            var e = sim.Entities;
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                float dx, dy; long key;
                if (sim.TryGetEnemySwing(i, out var swing) && swing.HitResolved)
                {
                    key = (1L << 40) | (long)swing.Serial;
                    dx = swing.Direction.X.ToFloat(); dy = swing.Direction.Y.ToFloat();
                }
                else if (sim.TryGetSplitterRoll(i, out var roll) && roll.Phase == SplitterRollPhase.Rolling)
                {
                    key = (2L << 40) | ((long)roll.Serial << 12) | (long)i;
                    dx = roll.Direction.X.ToFloat(); dy = roll.Direction.Y.ToFloat();
                }
                else if (sim.TryGetForestBudAttack(i, out var volley) && volley.ShotsFired > 0)
                {
                    key = (3L << 40) | (long)volley.Serial;
                    dx = e.Facing[i].X.ToFloat(); dy = e.Facing[i].Y.ToFloat();
                }
                else continue;
                if (!_seen.Add(key)) continue;
                float l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-6f) continue;
                dx /= l; dy /= l;
                float a = after.AngleTo(i, dx, dy), b = before.AngleTo(i, dx, dy);
                Count++;
                SumAfter += a;
                WorstAfter = Math.Max(WorstAfter, a);
                WorstBefore = Math.Max(WorstBefore, b);
                int k = (int)e.Kind[i];
                _byKind[k]++;
                _worstByKind[k] = Math.Max(_worstByKind[k], a);
            }
        }

        public void Report(StringBuilder b)
        {
            b.Append("  контакты: ").Append(Count).Append(", тело к удару — худший ")
                .Append(WorstAfter.ToString("0.0")).Append("° (было ").Append(WorstBefore.ToString("0.0"))
                .Append("°), средний ").Append((Count > 0 ? SumAfter / Count : 0f).ToString("0.0")).Append('°');
            for (int k = 0; k < _byKind.Length; k++)
                if (_byKind[k] > 0)
                    b.Append("; ").Append((EnemyKind)k).Append(' ').Append(_byKind[k]).Append(" худший ")
                        .Append(_worstByKind[k].ToString("0.0")).Append('°');
            b.Append('\n');
        }
    }

    private static string Percent(int part, int whole)
        => whole > 0 ? (100.0 * part / whole).ToString("0.0") + "%" : "—";

    /// <summary>
    /// Сцена CrowdStepHashPinTests (лесная арена 6, сид 42, 48 мобов на кольце 6–10 м): «круг» —
    /// бессмертный герой ходит по кругу 5 м; «бой» — эталонный герой по 4 с бьёт ближайшего и
    /// по 4 с ходит; «проходы» — бессмертный герой бегает 12 м туда-обратно сквозь толпу.
    /// </summary>
    private sealed class Scene
    {
        private const ulong Seed = 42;
        private const int Arena = 6;
        private const int Count = 48;

        private static readonly EnemyKind[] Dozen =
        {
            EnemyKind.ForestRootSwarm, EnemyKind.ForestGuardian, EnemyKind.ForestRootSwarm, EnemyKind.ForestBud,
            EnemyKind.ForestRootSwarm, EnemyKind.ForestStonehoof, EnemyKind.ForestRootSwarm, EnemyKind.ForestRootSnarer,
            EnemyKind.ForestGuardian, EnemyKind.ForestSplitter, EnemyKind.ForestRootSwarm, EnemyKind.ForestThorncaster,
        };

        public readonly Simulation Sim;
        private readonly LayoutMap _map;
        private readonly FixVec2 _center, _passA, _passB;
        private readonly Fix64 _heroBody;
        private readonly Hero _hero;
        private bool _toB = true;

        public Scene(Hero hero)
        {
            _hero = hero;
            var location = ForestLocation();
            _map = ArenaMap(location, Arena, Seed);
            Sim = new Simulation(Seed, 512);
            bool fight = hero == Hero.Fight;
            if (fight) Sim.ApplyHeroBaseline();
            Sim.SetupRift(_map, Seed, 0, 0, 100);
            Sim.BigAttackTokenLimit = Simulation.BigAttackTokensForArena(Arena);
            Sim.BigMarkBudget = Simulation.BigMarkBudgetForArena(Arena);
            Sim.PlayerInvulnerable = !fight;
            _heroBody = Sim.Entities.BodyRadius[Simulation.PlayerId];
            _center = _map.ClampToWalkable(_map.GetGlade(0).Center, _heroBody);
            _passA = _map.ClampToWalkable(_center + new FixVec2(Fix64.FromInt(-6), Fix64.Zero), _heroBody);
            _passB = _map.ClampToWalkable(_center + new FixVec2(Fix64.FromInt(6), Fix64.Zero), _heroBody);
            Sim.Entities.Position[Simulation.PlayerId] = _center;
            for (int i = 0; i < Count; i++)
            {
                EnemyKind kind = Dozen[i % Dozen.Length];
                if (kind == EnemyKind.ForestThorncaster && (i / Dozen.Length) % 2 == 1) kind = EnemyKind.ForestWendigo;
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio((i * 618) % 1000, 1000);
                Fix64 radius = Fix64.FromInt(6) + Fix64.Ratio(i % 5, 1);
                FixVec2 at = _map.ClampToWalkable(_center + FixVec2.FromAngle(angle) * radius, Fix64.One);
                Sim.AddKindTestEnemy(kind, at, 100);
            }
        }

        public void Step(int t)
        {
            var e = Sim.Entities;
            var input = InputFrame.Empty;
            bool fight = _hero == Hero.Fight;
            int target = fight && (t / 120) % 2 == 0 ? Nearest() : -1;
            if (target > 0)
            {
                input.Flags = (byte)InputFlags.Attack;
                input.AttackTarget = target;
                input.Aim = e.Position[target];
            }
            else if (_hero == Hero.Pass)
            {
                // Проходы 12 м туда-обратно: «бежим вдоль них».
                if (FixVec2.DistanceSq(e.Position[Simulation.PlayerId], _toB ? _passB : _passA) < Fix64.Ratio(1, 4)) _toB = !_toB;
                input.Flags = (byte)InputFlags.MoveOrder;
                input.Aim = _toB ? _passB : _passA;
            }
            else
            {
                // Приказ идти по кругу: точка на 40° впереди на окружности 5 м.
                input.Flags = (byte)InputFlags.MoveOrder;
                Fix64 lead = Fix64.TwoPi * Fix64.Ratio(t, 600) + Fix64.Ratio(7, 10);
                input.Aim = _map.ClampToWalkable(_center + FixVec2.FromAngle(lead) * Fix64.FromInt(5), _heroBody);
            }
            Sim.Step(input);
            if (fight && e.Alive[Simulation.PlayerId]) e.Health[Simulation.PlayerId] = e.MaxHealth[Simulation.PlayerId];
        }

        private int Nearest()
        {
            var e = Sim.Entities;
            FixVec2 hero = e.Position[Simulation.PlayerId];
            int best = -1;
            Fix64 bestDistance = Fix64.MaxValue;
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                Fix64 d = FixVec2.DistanceSq(hero, e.Position[i]);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        // Те же лес и арена, что у ArenaEncounterTests (Game.Tests в эту сборку не входит).
        private static LocationDefinition ForestLocation(int arenas = 8)
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            var levels = new RiftLevelSettings[arenas + 1];
            for (int i = 0; i < levels.Length; i++)
            {
                int arena = i + 1;
                bool boss = i == arenas;
                var settings = new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) },
                    new[] { new EncounterPack(2, 100, new[] { guardian }) }, new[] { new EncounterPack(3, 100, new[] { guardian }) },
                    new[] { new EncounterPack(4, 100, new[] { guardian }) },
                    1, 0, EnemyArchetypes.DepthDamagePercent(arena), Fix64.FromInt(5));
                levels[i] = new RiftLevelSettings(boss ? 20 : 11 + i, 1, 1, 2, 1, 3, EnemyArchetypes.DepthHealthPercent(arena),
                    settings, boss, playerHealth: 150, entryClearance: 14, solidEnvironment: true, naturalGlade: true)
                    .WithArenaSize(boss ? 4 : 3);
            }
            return new LocationDefinition(StableId.Of("location.test-forest"), PrototypeContent.Modules(), levels,
                64, completeAtEnd: true);
        }

        private static LayoutMap ArenaMap(LocationDefinition location, int level, ulong seed, int size = 3)
        {
            var settings = location.GetLevel(level);
            if (!settings.Boss) settings = settings.WithArenaSize(size);
            var map = new LayoutMap(location.Modules, location.MaxModules);
            settings.Generate(new LayoutGenerator(), location.Modules, map, seed);
            return map;
        }
    }
}
