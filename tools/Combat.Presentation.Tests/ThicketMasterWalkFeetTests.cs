using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Sim;
using Game.View;
using NUnit.Framework;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using Walk = Game.View.ThicketWalkRules;

/// <summary>
/// Хозяин Чащи — лапы на ходу (владелец 08.10: «ноги у босса немного проскальзывают при ходьбе»), ThicketWalkRules.
/// Замер 08.10 (artifacts/tools/boss-feet): клип Walk ведёт стоящие лапы назад на 2,1477 м за цикл при теле ×1,2027 —
/// ровно шаг префаба, по прямой лапы стояли. Скользили на ходу с поворотом: передние лапы стоят на 2,5 м впереди центра,
/// вокруг которого Sim крутит тело. Здесь: шаг и фаза по прямой (2,0 / 2,8 / пыльца −30%, разгон) — путь / шаг, стоящая
/// лапа стоит; на ходу с поворотом лапа держит землю замком (прежнее правило тащило её на десятки сантиметров);
/// цели лап без скачков; двухкостная ИК ставит конец ноги в цель, не меняя длины костей и стороны колена; живая Sim.
/// Лапы — синтетический клип: стопа стоит по окну опоры правила и едет назад ровно на шаг клипа за цикл.
/// </summary>
public sealed class ThicketMasterWalkFeetTests
{
    private const float Dt = 1f / 60f;

    /// <summary>Поправка замка лапы меняется за кадр 60 Гц не больше: без скачков на касании, отрыве и затухании.</summary>
    private const float JumpLimit = .06f;
    private static readonly float Scale = Walk.MeasuredBodyScale;

    /// <summary>Шаг клипа в осях корня префаба: замер стоящих лап Walk.anim (walk_feet.py) — 2,1477 м/цикл.</summary>
    private static readonly float Stride = ThicketMasterClipRules.DefaultWalkStride * Walk.MeasuredBodyScale;

    // ------------------------------------------------------------ шаг и фаза

    [Test]
    public void Stride_PrefabStrideIsTheClipsPlantedTravelAtThePrefabBodyScale()
    {
        Assert.That(Stride, Is.EqualTo(2.1477f).Within(.002f), "замер 08.10: стоящие лапы клипа — 2,1477 м за цикл 1 с");
        string path = Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "Characters", "Forest_ThicketMaster",
            "ThicketMaster_Runtime.prefab");
        if (!File.Exists(path)) Assert.Ignore("префаба Хозяина Чащи нет");
        string prefab = File.ReadAllText(path);
        var strideMatch = Regex.Match(prefab, @"_walkStride:\s*([0-9.]+)");
        var scaleMatch = Regex.Match(prefab, @"propertyPath: m_LocalScale\.x\s+value: ([0-9.]+)");
        Assert.That(strideMatch.Success && scaleMatch.Success, Is.True, "в префабе нет _walkStride или масштаба тела");
        float stride = float.Parse(strideMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        float scale = float.Parse(scaleMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.That(stride / scale, Is.EqualTo(ThicketMasterClipRules.DefaultWalkStride).Within(.002f),
            "шаг префаба = шаг клипа в масштабе модели × масштаб тела: иначе лапы скользят на ту же долю");
    }

    [Test]
    public void Cadence_StraightIsPathOverStride_TurningStepsMoreOften_NeverAboveTheCap()
    {
        float straight20 = Walk.CyclesPerSecond(2f, 0f, 0f, Stride, Scale);
        float straight28 = Walk.CyclesPerSecond(2.8f, 0f, 0f, Stride, Scale);
        Assert.That(straight20, Is.EqualTo(2f / Stride).Within(1e-5), "2,0 м/с — 0,93 цикла в секунду, как было");
        Assert.That(straight28, Is.EqualTo(2.8f / Stride).Within(1e-5), "дальний 2,8 м/с — 1,30");
        Assert.That(Walk.CyclesPerSecond(1.4f, 0f, 0f, Stride, Scale), Is.EqualTo(1.4f / Stride).Within(1e-5), "пыльца −30%");
        foreach (float yaw in new[] { -75f, -30f, 10f, 40f, 75f })
        {
            float turning = Walk.CyclesPerSecond(2f, 0f, yaw, Stride, Scale);
            Assert.That(turning, Is.GreaterThan(straight20), "на повороте передняя наружная лапа проходит больше земли — шагает чаще");
            Assert.That(turning, Is.LessThanOrEqualTo(Walk.MaxCyclesPerSecond + 1e-6f));
            Assert.That(turning, Is.GreaterThanOrEqualTo(Math.Abs(yaw) / ThicketMasterClipRules.WalkTurnDegreesPerCycle),
                "не реже прежнего пола «90° поворота на цикл» (ревью 02.10, вечер: не крутиться на стоящих лапах)");
        }
        Assert.That(Walk.CyclesPerSecond(0f, 0f, 0f, Stride, Scale), Is.EqualTo(0f));
    }

    [TestCase(2.0f)]
    [TestCase(2.8f)]
    [TestCase(1.4f)]
    [TestCase(1.96f)]
    public void Straight_PhaseIsPathOverStride_PlantedPawsStand_NoCorrection(float speed)
    {
        var walker = new Walker(newRule: true);
        for (int f = 0; f < 240; f++) walker.Frame(Dt, speed, 0f);
        Assert.That(walker.Phase, Is.EqualTo(speed * 240 * Dt / Stride).Within(1e-3), "фаза — ровно путь / шаг");
        Assert.That(walker.Stances, Is.GreaterThanOrEqualTo(8), "лапы успели постоять");
        Assert.That(walker.MaxDrift, Is.LessThan(.005f), $"стоящая лапа уехала на {walker.MaxDrift * 100:0.0} см");
        Assert.That(walker.MaxCorrection, Is.LessThan(.001f), "по прямой ИК ничего не правит — клип и так стоит");
    }

    [Test]
    public void SpeedChanges_DistanceDrivesThePhase_PlantedPawsStand()
    {
        // Разгон Sim 3 тика до 2,0, полоса дальнего — 2,8 за 3 тика, пыльца −30%, остановка.
        var walker = new Walker(newRule: true);
        float distance = 0f;
        for (int f = 0; f < 600; f++)
        {
            float t = f * Dt;
            float speed = t < .1f ? 20f * t : t < 3f ? 2f : t < 3.1f ? 2f + 8f * (t - 3f) : t < 6f ? 2.8f : t < 8f ? 1.96f : 1.4f;
            walker.Frame(Dt, speed, 0f);
            distance += speed * Dt;
        }
        Assert.That(walker.Phase, Is.EqualTo(distance / Stride).Within(1e-3));
        Assert.That(walker.MaxDrift, Is.LessThan(.005f), $"стоящая лапа уехала на {walker.MaxDrift * 100:0.0} см");
    }

    // ------------------------------------------------------------ поворот на ходу

    [TestCase(2.0f, 20f)]
    [TestCase(2.0f, 40f)]
    [TestCase(2.8f, 30f)]
    [TestCase(2.8f, -45f)]
    [TestCase(1.4f, 60f)]
    [TestCase(2.0f, 75f)]
    public void Turning_PlantedPawsHoldTheGround_OldRuleDraggedThem(float speed, float yawRate)
    {
        var fixedFeet = new Walker(newRule: true);
        var old = new Walker(newRule: false);
        for (int f = 0; f < 300; f++)
        {
            fixedFeet.Frame(Dt, speed, yawRate);
            old.Frame(Dt, speed, yawRate);
        }
        TestContext.WriteLine($"{speed} м/с, {yawRate}°/с: стоящая передняя лапа — было {old.MaxFrontDrift * 100:0} см, стало " +
                              $"{fixedFeet.MaxFrontDrift * 100:0.0} см; задняя {old.MaxHindDrift * 100:0} → {fixedFeet.MaxHindDrift * 100:0.0} см; " +
                              $"поправка ИК до {fixedFeet.MaxCorrection * 100:0} см (скачок за кадр до {fixedFeet.MaxJump * 100:0.0}), " +
                              $"{fixedFeet.Feet.CyclesPerSecond:0.00} цикла/с");
        Assert.That(old.MaxFrontDrift, Is.GreaterThan(.2f), "прежнее правило тащит стоящую переднюю лапу — жалоба воспроизводится");
        Assert.That(fixedFeet.MaxFrontDrift, Is.LessThan(.01f), "стоящая передняя лапа держит землю");
        Assert.That(fixedFeet.MaxHindDrift, Is.LessThan(.01f), "стоящая задняя лапа держит землю");
        Assert.That(fixedFeet.MaxCorrection, Is.LessThan(1.0f), "поправка ИК в пределах шага (лапы 2 м)");
        Assert.That(fixedFeet.MaxJump, Is.LessThan(JumpLimit), $"поправка лапы прыгнула на {fixedFeet.MaxJump * 100:0} см за кадр");
    }

    [Test]
    public void TurnRateChanges_TargetsStayContinuous_LockReleasesInTheAir()
    {
        // Герой бегает вбок: поворот то вправо, то влево, ход то 2,0, то 2,8.
        var walker = new Walker(newRule: true);
        for (int f = 0; f < 900; f++)
        {
            float t = f * Dt;
            float yaw = 60f * (float)Math.Sin(t * 1.7f);
            // Полоса дальнего: 2,0 ↔ 2,8 м/с за 3 тика Sim (AccelerationTicks), как разгон Sim.
            float u = t % 4f, speed = u < 2f ? 2f + .8f * Math.Min(1f, u * 10f) : 2.8f - .8f * Math.Min(1f, (u - 2f) * 10f);
            walker.Frame(Dt, speed, yaw);
        }
        Assert.That(walker.MaxFrontDrift, Is.LessThan(.01f));
        Assert.That(walker.MaxHindDrift, Is.LessThan(.01f));
        Assert.That(walker.MaxJump, Is.LessThan(JumpLimit), $"поправка лапы прыгнула на {walker.MaxJump * 100:0} см за кадр");
        Assert.That(walker.MaxReleaseLeft, Is.LessThan(1e-4f), "через ReleaseCycles после отрыва поправка замка погасла");
    }

    [Test]
    public void Lock_SlidesAfterTheWarpBeyondReach()
    {
        var feet = new ThicketWalkFeet();
        feet.Advance(Dt, 2f * Dt, 0f, 0f, Stride, Scale);
        int paw = Walk.FrontLeft;
        float phase = Walk.LockStart(paw) + .01f;
        float cx = Walk.StanceX(paw, Scale), cz = Walk.StanceZ(paw, Scale);
        feet.Place(paw, phase, cx, cz, out float tx, out float tz, out _);
        Assert.That(feet.Locked(paw), Is.True);
        Assert.That(tx, Is.EqualTo(cx).Within(1e-5));
        // Корень уехал на 2 м вбок за кадр (дальше досягаемого): замок держится в MaxLockMetres от шага, а не рвёт ногу.
        feet.Follow(0f, 2f, 0f);
        feet.Place(paw, phase + .001f, cx, cz, out tx, out tz, out _);
        float reach = Walk.MaxLockMetres * Scale;
        Assert.That(Math.Sqrt((tx - cx) * (tx - cx) + (tz - cz) * (tz - cz)), Is.EqualTo(reach).Within(1e-3));
        Assert.That(tx, Is.LessThan(cx), "замок остался сзади-слева — с той стороны, где земля");
    }

    [Test]
    public void Follow_IsTheInverseRootMotion()
    {
        // Точка мира и корень (позиция, взгляд по часовой от +Z); корень прошёл вперёд/вбок в своих осях и повернулся.
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            float px = R(rng, 5), pz = R(rng, 5), heading = R(rng, 180), wx = R(rng, 6), wz = R(rng, 6);
            float forward = R(rng, .2f), side = R(rng, .1f), yaw = R(rng, 5);
            ToLocal(wx, wz, px, pz, heading, out float qx, out float qz);
            Rotate(heading, out float rx, out float rz, out float fx, out float fz);
            float nx = px + rx * side + fx * forward, nz = pz + rz * side + fz * forward;
            ToLocal(wx, wz, nx, nz, heading + yaw, out float ex, out float ez);
            Walk.Follow(ref qx, ref qz, forward, side, yaw);
            Assert.That(qx, Is.EqualTo(ex).Within(1e-4));
            Assert.That(qz, Is.EqualTo(ez).Within(1e-4));
        }
    }

    [Test]
    public void Windows_FrontHoldsTheKnuckle_HindTheFlatFoot()
    {
        for (int paw = 0; paw < Walk.PawCount; paw++)
        {
            float span = Walk.LockSpan(paw);
            Assert.That(span, Is.EqualTo(Walk.IsFront(paw) ? .44f : .29f).Within(1e-4), "окно опоры лапы " + paw);
            Assert.That(Walk.Planted(paw, Walk.LockStart(paw) + .001f), Is.True);
            Assert.That(Walk.Planted(paw, Walk.LockStart(paw) + span + .001f), Is.False);
            Assert.That(Walk.Planted(paw, Walk.LockStart(paw) - .001f), Is.False);
        }
        // Диагональные пары: задняя правая с передней левой, задняя левая с передней правой (clip_walk.py).
        Assert.That(Walk.Planted(Walk.HindRight, .2f) && Walk.Planted(Walk.FrontLeft, .2f), Is.True);
        Assert.That(Walk.Planted(Walk.HindLeft, .7f) && Walk.Planted(Walk.FrontRight, .7f), Is.True);
        Assert.That(Walk.Planted(Walk.HindLeft, .2f) || Walk.Planted(Walk.FrontRight, .2f), Is.False);
        // Стоп-центры (замер foot_table.py 08.10, масштаб префаба): перед на 2,47 м впереди центра, зад на 0,80 позади.
        Assert.That(Walk.StanceZ(Walk.FrontLeft, Scale), Is.EqualTo(2.470f).Within(.002f));
        Assert.That(Walk.StanceZ(Walk.HindRight, Scale), Is.EqualTo(-.806f).Within(.005f));
        Assert.That(Walk.StanceX(Walk.FrontRight, Scale), Is.EqualTo(1.467f).Within(.002f));
        Assert.That(Walk.StanceX(Walk.HindLeft, Scale), Is.EqualTo(-1.419f).Within(.002f));
    }

    // ------------------------------------------------------------ ИК

    [Test]
    public void LegIk_EndReachesTheTarget_BonesKeepLength_KneeKeepsItsSide()
    {
        var rng = new Random(11);
        int solved = 0;
        for (int i = 0; i < 500; i++)
        {
            var a = new NVector3(R(rng, 1), 2f + R(rng, .3f), R(rng, 1));
            var knee = Dir(rng, new NVector3(0f, -1f, .4f), .5f);
            var b = a + knee * 1.07f;
            var shin = Dir(rng, new NVector3(0f, -1f, -.4f), .5f);
            var c = b + shin * 1.02f;
            var target = c + new NVector3(R(rng, .5f), R(rng, .2f), R(rng, .5f));
            if (!ThicketLegIk.Solve(a, b, c, target, out var bend, out var aim)) continue;
            solved++;
            // Как в виде: нижняя кость поворачивается вокруг колена, потом вся нога — вокруг плеча.
            var c1 = b + NVector3.Transform(c - b, bend);
            var b2 = a + NVector3.Transform(b - a, aim);
            var c2 = a + NVector3.Transform(c1 - a, aim);
            Assert.That((b2 - a).Length(), Is.EqualTo(1.07f).Within(1e-4));
            Assert.That((c2 - b2).Length(), Is.EqualTo(1.02f).Within(1e-4));
            float reach = (target - a).Length();
            if (reach < (1.07f + 1.02f) * ThicketLegIk.MaxReach - 1e-3f && reach > .1f)
                Assert.That((c2 - target).Length(), Is.LessThan(1e-3f), "конец ноги в цели");
            else
                Assert.That(NVector3.Dot(NVector3.Normalize(c2 - a), NVector3.Normalize(target - a)), Is.GreaterThan(.9999f), "недостать — нога тянется к цели");
            // Колено гнётся в ту же сторону: нормаль плоскости ноги после ИК — повёрнутая прежняя.
            var before = NVector3.Normalize(NVector3.Cross(a - b, c - b));
            var after = NVector3.Normalize(NVector3.Cross(a - b2, c2 - b2));
            Assert.That(NVector3.Dot(NVector3.Transform(before, aim), after), Is.GreaterThan(.999f), "колено не вывернулось");
        }
        Assert.That(solved, Is.GreaterThan(450));
        Assert.That(ThicketLegIk.Solve(NVector3.Zero, NVector3.Zero, NVector3.UnitY, NVector3.One, out _, out _), Is.False, "нулевая кость");
        Assert.That(ThicketLegIk.Solve(NVector3.Zero, NVector3.UnitY, 2 * NVector3.UnitY, NVector3.One, out _, out _), Is.False,
            "прямая нога без плоскости сгиба — не трогать");
    }

    [Test]
    public void LegIk_FromTo()
    {
        Assert.That(ThicketLegIk.FromTo(NVector3.UnitX, NVector3.UnitX), Is.EqualTo(NQuaternion.Identity));
        var q = ThicketLegIk.FromTo(NVector3.UnitX, NVector3.UnitZ);
        Assert.That((NVector3.Transform(NVector3.UnitX, q) - NVector3.UnitZ).Length(), Is.LessThan(1e-5f));
        var back = ThicketLegIk.FromTo(NVector3.UnitX, -NVector3.UnitX);
        Assert.That((NVector3.Transform(NVector3.UnitX, back) + NVector3.UnitX).Length(), Is.LessThan(1e-4f));
    }

    // ------------------------------------------------------------ живая Sim

    /// <summary>
    /// Живая Sim: герой бегает вбок перед боссом, потом кружит — босс идёт и доворачивает (как в сценах стенда
    /// artifacts/tools/boss-feet/slide). Тело вида — интерполяция TickDriver и взгляд ArenaView (SimFacing, резкость 20).
    /// Стоящая лапа синтетического клипа: прежнее правило (фаза по пути с полом поворота) тащило её в среднем на
    /// десятки сантиметров за стойку, новое держит её на земле.
    /// </summary>
    [Test]
    public void LiveBoss_HeroRunsAround_PlantedPawsHoldTheGround()
    {
        var oldSlips = Live(newRule: false);
        var newSlips = Live(newRule: true);
        float Median(List<float> v) => v.OrderBy(x => x).ElementAt(v.Count / 2);
        float P90(List<float> v) => v.OrderBy(x => x).ElementAt((int)(v.Count * .9));
        TestContext.WriteLine($"стоек {newSlips.Count}: было медиана {Median(oldSlips) * 100:0} см, p90 {P90(oldSlips) * 100:0}; " +
                              $"стало медиана {Median(newSlips) * 100:0.0} см, p90 {P90(newSlips) * 100:0.0}, макс {newSlips.Max() * 100:0.0}");
        Assert.That(newSlips.Count, Is.GreaterThan(60), "сцена не дошла до хода");
        Assert.That(Median(oldSlips), Is.GreaterThan(.1f), "сцена воспроизводит жалобу");
        Assert.That(Median(newSlips), Is.LessThan(.01f));
        Assert.That(P90(newSlips), Is.LessThan(.05f));
    }

    private static List<float> Live(bool newRule)
    {
        const int boss = 1;
        var sim = new Simulation(77, 64);
        sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(6));
        var e = sim.Entities;
        e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(1000000));
        e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
        e.RefreshStats(0);
        e.Health[0] = e.MaxHealth[0];
        FixVec2 home = e.Position[boss];
        var walker = new Walker(newRule);
        var slips = new List<float>();
        walker.OnStance = slip => slips.Add(slip);
        float acc = 0f, time = 0f;
        (float x, float y) prevPos = P(e.Position[boss]), curPos = prevPos, prevF = P(e.Facing[boss]), curF = prevF;
        float visualX = curF.x, visualY = curF.y, lastBodyX = visualX, lastBodyY = visualY;
        float turnSign = 0f, turnUntil = -1f;
        bool turnShown = false, walking = false;
        var latch = EnemyBodyLatch.None;
        while (sim.Tick < 30 * 50)
        {
            time += Dt;
            acc += Dt;
            while (acc >= 1f / 30f)
            {
                prevPos = P(e.Position[boss]);
                prevF = P(e.Facing[boss]);
                int tick = sim.Tick;
                double s = Math.Max(0, tick - 60) / 30.0;
                e.Position[0] = tick < 60 ? home + V(-4, 0)
                    : tick < 750 ? home + V(-7, Tri(s * 4.0, 6.0))
                    : home + V(7 * Math.Cos(Math.PI + s * 3.0 / 7.0), 7 * Math.Sin(Math.PI + s * 3.0 / 7.0));
                e.Health[0] = e.MaxHealth[0];
                sim.Step(InputFrame.Empty);
                curPos = P(e.Position[boss]);
                curF = P(e.Facing[boss]);
                acc -= 1f / 30f;
            }
            float alpha = acc * 30f;
            float rootX = prevPos.x + (curPos.x - prevPos.x) * alpha, rootZ = prevPos.y + (curPos.y - prevPos.y) * alpha;
            Slerp(prevF, curF, alpha, out float sx, out float sz);
            // Вид (Update): поворот — по взгляду тела прошлого LateUpdate (Unity SignedAngle: по часовой сверху).
            float yaw = -EnemyBodyFacingRules.SignedAngle(lastBodyX, lastBodyY, visualX, visualY);
            lastBodyX = visualX; lastBodyY = visualY;
            float beforeX = visualX, beforeY = visualY;
            var target = EnemyBodyFacingRules.Target(EnemyBodyPolicy.SimFacing, sx, sz, 0f, 0f, 0f, false, ref latch);
            EnemyBodyFacingRules.Step(ref visualX, ref visualY, target, Dt);

            bool acting = sim.TryGetThicketMasterAction(boss, out _);
            float speed = e.Velocity[boss].Length.ToFloat() * Simulation.TicksPerSecond;
            var motion = ThicketMotion.Idle;
            if (!acting && sim.ThicketMasterAwake(boss))
            {
                if (Math.Abs(yaw) / Dt > ThicketMasterClipRules.TurnRateThreshold)
                {
                    float sign = Math.Sign(yaw);
                    if (sign != turnSign) turnSign = sign;
                    turnUntil = time + .2f;
                }
                bool turning = turnSign != 0f && time <= turnUntil;
                if (!turning) { turnSign = 0f; turnShown = false; }
                motion = ThicketMasterClipRules.Locomotion(speed, turning, turnShown);
                turnShown = motion == ThicketMotion.Turn;
            }
            if (motion != ThicketMotion.Walk)
            {
                if (walking) walker.Stop();
                walking = false;
                walker.Place(rootX, rootZ, visualX, visualY);
                continue;
            }
            walking = true;
            float dx = rootX - walker.RootX, dz = rootZ - walker.RootZ;
            walker.Advance(Dt, dx * beforeX + dz * beforeY, dx * beforeY - dz * beforeX, yaw, speed);
            float lyaw = -EnemyBodyFacingRules.SignedAngle(beforeX, beforeY, visualX, visualY);
            walker.Move(rootX, rootZ, visualX, visualY, dx * beforeX + dz * beforeY, dx * beforeY - dz * beforeX, lyaw);
        }
        TestContext.WriteLine($"{(newRule ? "новое" : "прежнее")}: кадров опоры {walker.PlantedFrames}, замок у предела {walker.ClampFrames}");
        return slips;
    }

    // ------------------------------------------------------------ ходок: корень, синтетический клип, правило

    /// <summary>
    /// Корень в мире (X — вправо, Z — вперёд мира), взгляд по часовой от +Z; лапы — синтетический клип: стопа стоит в
    /// окне опоры правила и едет назад ровно на шаг клипа за цикл, вокруг стоп-центра лапы. Мерит, на сколько стоящая
    /// лапа уезжает от точки касания в мире, поправку ИК (цель − клип) и скачки цели за кадр.
    /// </summary>
    private sealed class Walker
    {
        public readonly ThicketWalkFeet Feet = new ThicketWalkFeet();
        private readonly bool _newRule;
        public float RootX, RootZ, ForwardX, ForwardZ = 1f, Phase;
        /// <summary>Стоящая лапа уехала от касания (перед/зад), поправка ИК, её скачок за кадр, остаток замка после отрыва, м.</summary>
        public float MaxFrontDrift, MaxHindDrift, MaxCorrection, MaxJump, MaxReleaseLeft;
        public int Stances, ClampFrames, PlantedFrames;
        public Action<float> OnStance;
        private readonly bool[] _on = new bool[Walk.PawCount], _seen = new bool[Walk.PawCount];
        private readonly float[] _touchX = new float[Walk.PawCount], _touchZ = new float[Walk.PawCount];
        private readonly float[] _lastX = new float[Walk.PawCount], _lastZ = new float[Walk.PawCount];
        private readonly float[] _corrX = new float[Walk.PawCount], _corrZ = new float[Walk.PawCount];
        private readonly float[] _standX = new float[Walk.PawCount], _standZ = new float[Walk.PawCount];

        public Walker(bool newRule) { _newRule = newRule; }

        public float MaxDrift => Math.Max(MaxFrontDrift, MaxHindDrift);

        /// <summary>Кадр по заданному ходу (м/с) и повороту (°/с): корень идёт по прежнему взгляду, потом поворачивается.</summary>
        public void Frame(float dt, float speed, float yawRate)
        {
            float forward = speed * dt, yaw = yawRate * dt;
            Advance(dt, forward, 0f, yaw, speed);
            float x = RootX + ForwardX * forward, z = RootZ + ForwardZ * forward;
            double h = Math.Atan2(ForwardX, ForwardZ) + yaw * Math.PI / 180.0;
            Move(x, z, (float)Math.Sin(h), (float)Math.Cos(h), forward, 0f, yaw);
        }

        /// <summary>Update вида: фаза Walk этого кадра.</summary>
        public void Advance(float dt, float forward, float side, float yaw, float speed)
        {
            Phase += _newRule
                ? Feet.Advance(dt, forward, side, yaw, Stride, Scale)
                : ThicketMasterClipRules.WalkCyclesTurning(speed * dt, yaw, Stride, 1f);
        }

        /// <summary>LateUpdate вида: корень встал в (x, z) со взглядом (fx, fz), сдвинувшись в прежних осях и повернувшись.</summary>
        public void Move(float x, float z, float fx, float fz, float forward, float side, float yaw)
        {
            if (_newRule) Feet.Follow(forward, side, yaw);
            RootX = x; RootZ = z; ForwardX = fx; ForwardZ = fz;
            float phase = Phase - (float)Math.Floor(Phase);
            for (int paw = 0; paw < Walk.PawCount; paw++)
            {
                Clip(paw, phase, out float cx, out float cz);
                float tx = cx, tz = cz;
                if (_newRule) Feet.Place(paw, phase, cx, cz, out tx, out tz, out _);
                MaxCorrection = Math.Max(MaxCorrection, Length(tx - cx, tz - cz));
                World(tx, tz, out float wx, out float wz);
                // Скачок — поправка замка (цель − шаг под поворот, оси корня) сменилась за кадр: на касании, отрыве и
                // затухании она не прыгает (шаг под поворот сам по себе — плавная функция клипа).
                float lx = 0f, lz = 0f;
                if (_newRule)
                {
                    Walk.Warp(paw, cx, cz, Feet.WarpForward, Feet.WarpSide, Feet.YawRate, Feet.CyclesPerSecond, Stride, Scale, out float sx, out float sz);
                    lx = tx - sx; lz = tz - sz;
                }
                if (_seen[paw]) MaxJump = Math.Max(MaxJump, Length(lx - _corrX[paw], lz - _corrZ[paw]));
                _corrX[paw] = lx; _corrZ[paw] = lz;
                _seen[paw] = true;
                _lastX[paw] = wx; _lastZ[paw] = wz;
                bool planted = Walk.Planted(paw, phase);
                if (planted && !_on[paw]) { _on[paw] = true; _touchX[paw] = _standX[paw] = wx; _touchZ[paw] = _standZ[paw] = wz; continue; }
                if (!planted && _on[paw])
                {
                    // Стойка кончилась: сдвиг от касания в последнем кадре опоры (этот кадр — уже отрыв).
                    _on[paw] = false;
                    Stances++;
                    OnStance?.Invoke(Length(_standX[paw] - _touchX[paw], _standZ[paw] - _touchZ[paw]));
                    continue;
                }
                if (!planted)
                {
                    if (_newRule && Walk.SinceLift(paw, phase) > Walk.ReleaseCycles + .02f && Walk.SinceLift(paw, phase) < .5f)
                    {
                        // Поправка замка погасла: цель — шаг под поворот без остатка.
                        Walk.Warp(paw, cx, cz, Feet.WarpForward, Feet.WarpSide, Feet.YawRate, Feet.CyclesPerSecond, Stride, Scale, out float wx2, out float wz2);
                        MaxReleaseLeft = Math.Max(MaxReleaseLeft, Length(tx - wx2, tz - wz2));
                    }
                    continue;
                }
                _standX[paw] = wx; _standZ[paw] = wz;
                float drift = Length(wx - _touchX[paw], wz - _touchZ[paw]);
                if (_newRule && Length(lx, lz) > Walk.MaxLockMetres * Scale * .99f) ClampFrames++;
                PlantedFrames++;
                if (Walk.IsFront(paw)) MaxFrontDrift = Math.Max(MaxFrontDrift, drift);
                else MaxHindDrift = Math.Max(MaxHindDrift, drift);
            }
        }

        /// <summary>Не ход (стоит, разворот, действие): стойки обрываются, корень просто встаёт на место.</summary>
        public void Stop()
        {
            Array.Clear(_on, 0, _on.Length);
            Array.Clear(_seen, 0, _seen.Length);
            Feet.Reset();
        }

        public void Place(float x, float z, float fx, float fz) { RootX = x; RootZ = z; ForwardX = fx; ForwardZ = fz; }

        private void World(float x, float z, out float wx, out float wz)
        {
            // Вправо от взгляда (fx, fz) по часовой сверху: (fz, −fx).
            wx = RootX + ForwardZ * x + ForwardX * z;
            wz = RootZ - ForwardX * x + ForwardZ * z;
        }
    }

    /// <summary>Синтетический клип: стопа стоит в окне опоры и едет назад на шаг за цикл, вокруг стоп-центра; в воздухе — вперёд.</summary>
    private static void Clip(int paw, float phase, out float x, out float z)
    {
        float cx = Walk.StanceX(paw, Scale), cz = Walk.StanceZ(paw, Scale);
        float span = Walk.LockSpan(paw), s = Walk.SinceLock(paw, phase), half = Stride * span / 2f;
        x = cx;
        z = s < span ? cz + half - Stride * s : cz - half + 2f * half * (s - span) / (1f - span);
    }

    // ------------------------------------------------------------ мелочи

    private static float R(Random rng, float range) => (float)((rng.NextDouble() * 2 - 1) * range);

    private static NVector3 Dir(Random rng, NVector3 around, float spread)
        => NVector3.Normalize(around + new NVector3(R(rng, spread), R(rng, spread), R(rng, spread)));

    private static float Length(float x, float z) => (float)Math.Sqrt(x * x + z * z);

    private static void Rotate(float heading, out float rx, out float rz, out float fx, out float fz)
    {
        double h = heading * Math.PI / 180.0;
        fx = (float)Math.Sin(h); fz = (float)Math.Cos(h);
        rx = fz; rz = -fx;
    }

    private static void ToLocal(float wx, float wz, float px, float pz, float heading, out float x, out float z)
    {
        Rotate(heading, out float rx, out float rz, out float fx, out float fz);
        float dx = wx - px, dz = wz - pz;
        x = dx * rx + dz * rz;
        z = dx * fx + dz * fz;
    }

    private static (float x, float y) P(FixVec2 v) => (v.X.ToFloat(), v.Y.ToFloat());

    private static FixVec2 V(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

    private static double Tri(double s, double amp)
    {
        double period = 4 * amp, u = s % period;
        return u < amp ? u : u < 3 * amp ? 2 * amp - u : u - 4 * amp;
    }

    private static void Slerp((float x, float y) a, (float x, float y) b, float t, out float x, out float y)
    {
        double aa = Math.Atan2(a.y, a.x), bb = Math.Atan2(b.y, b.x), d = bb - aa;
        while (d > Math.PI) d -= 2 * Math.PI;
        while (d < -Math.PI) d += 2 * Math.PI;
        double r = aa + d * t;
        x = (float)Math.Cos(r);
        y = (float)Math.Sin(r);
    }
}
