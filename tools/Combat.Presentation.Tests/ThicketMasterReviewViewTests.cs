using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;
using Intro = Game.View.ThicketMasterIntroRules;
using Rules = Game.View.ThicketMasterClipRules;

/// <summary>
/// Хозяин Чащи — вид по ревью владельца и Кости 02.10 (вечер), контракт artifacts/tools/wf/boss-tempo-contract.md § 9:
/// • «анимации разворота нет нормальной… закруживаешь его, и он тупо прокручивается на месте» — правило
///   ThicketMasterClipRules.Locomotion: стоит или ползёт, а корпус крутится, — шаги TurnL/TurnR по углу (Sim
///   2,5°/тик, клип ×0,83); идёт и доворачивает — Walk не медленнее поворота. Живая сцена: герой кружит
///   вокруг босса — ни одного тика, где тело Sim поворачивается, а лапы стоят (раньше Walk по пройденному
///   пути почти стоял, пока Sim поворачивал и полз вперёд на 0,1–0,5 м/с);
/// • «замедлить тычку лапой на 10% и обозначить» — замах 17 / 10 ложится на клип (контакт — кадр 24), лента
///   когтей начинается на одном кадре клипа при любом замахе, метка-сектор каждого удара на земле —
///   SharedView из бьющего плеча, ровно от знака до удара;
/// • «в кат-сцене виден HUD и миникарта» — холсты HUD выключены всё окно и возвращаются после E.
/// Проверка находок 03.10 (баланс 02.10, ночь: корпус в серии лапы 3,5°/тик, промежутки серии 8–12): под ударом
/// лапы задние лапы и опорная передняя переступают шагами разворота (ThicketPawTurnLegs), замах и удар шага,
/// прочитанного позже, — из полей серии.
/// </summary>
public sealed class ThicketMasterReviewViewTests
{
    private const int Boss = 1;

    // ------------------------------------------------------------ разворот

    [Test]
    public void Turn_SimRate_ClipPlaysByAngle()
    {
        Assert.That(Simulation.ThicketTurnTicksPerCircle, Is.EqualTo(144), "контракт § 9: 2,5°/тик");
        Assert.That(Rules.SimTurnDegreesPerTick, Is.EqualTo(2.5f).Within(1e-5));
        Assert.That(Rules.TurnPlayback(Rules.SimTurnDegreesPerTick), Is.EqualTo(30f / 36f).Within(1e-4),
            "клип 90° на 30 кадров при 2,5°/тик — ×0,83 (контракт § 9)");
        Assert.That(Rules.SimTurnDegreesPerTick / (1f / 30f), Is.GreaterThan(Rules.TurnRateThreshold),
            "поворот Sim (75°/с) — выше порога «крутится»");

        // 90° поворота Sim — ровно один клип за 36 тиков, кадры не идут назад.
        float travel = 0f, last = -1f;
        for (int t = 1; t <= 36; t++)
        {
            travel += Rules.SimTurnDegreesPerTick;
            float phase = Rules.TurnPhase(travel);
            Assert.That(phase, Is.GreaterThan(last), "тик " + t);
            last = phase;
        }
        Assert.That(last, Is.EqualTo(1f), "90° — конец клипа");
    }

    [Test]
    public void Locomotion_StandsAndTurns_CreepsAndTurns_WalksAndTurns()
    {
        Assert.That(Rules.Locomotion(0f, false, false), Is.EqualTo(ThicketMotion.Idle));
        Assert.That(Rules.Locomotion(0f, true, false), Is.EqualTo(ThicketMotion.Turn), "на месте — шаги разворота");
        Assert.That(Rules.Locomotion(Rules.WalkThresholdSpeed, true, false), Is.EqualTo(ThicketMotion.Turn));
        Assert.That(Rules.Locomotion(.3f, false, false), Is.EqualTo(ThicketMotion.Walk), "идёт прямо — ход");
        Assert.That(Rules.Locomotion(.3f, true, false), Is.EqualTo(ThicketMotion.Turn), "ползёт, крутясь, — разворот");
        Assert.That(Rules.Locomotion(.6f, true, false), Is.EqualTo(ThicketMotion.Walk), "разгон выше входа — ход");
        Assert.That(Rules.Locomotion(.6f, true, true), Is.EqualTo(ThicketMotion.Turn), "в развороте держится до выхода");
        Assert.That(Rules.Locomotion(.8f, true, true), Is.EqualTo(ThicketMotion.Walk));
        float full = Simulation.ThicketMasterMoveSpeed.ToFloat();
        Assert.That(Rules.Locomotion(full, true, true), Is.EqualTo(ThicketMotion.Walk), "полный ход с поворотом — ход");
        Assert.That(Rules.TurnInPlaceEnterSpeed, Is.LessThan(Rules.TurnInPlaceExitSpeed), "гистерезис");
        Assert.That(Rules.TurnInPlaceExitSpeed, Is.LessThan(full * .5f));

        // Ход с поворотом: лапы не реже поворота, прямой ход — как раньше.
        float stride = Rules.DefaultWalkStride;
        float metres = full / 30f;
        Assert.That(Rules.WalkCyclesTurning(metres, 0f, stride, 1.15f), Is.EqualTo(Rules.WalkCycles(metres, stride, 1.15f)));
        Assert.That(Rules.WalkCyclesTurning(.01f, -2.5f, stride, 1.15f), Is.EqualTo(2.5f / Rules.WalkTurnDegreesPerCycle).Within(1e-6));
        Assert.That(Rules.WalkCyclesTurning(metres, 2.5f, stride, 1.15f),
            Is.EqualTo(Math.Max(Rules.WalkCycles(metres, stride, 1.15f), 2.5f / Rules.WalkTurnDegreesPerCycle)).Within(1e-6));

        // Хвост клипа переживает доворот на пару тиков, настоящий поворот его обрывает.
        Assert.That(Rules.TurnBreaksTail(2 * Rules.SimTurnDegreesPerTick), Is.False);
        Assert.That(Rules.TurnBreaksTail(4 * Rules.SimTurnDegreesPerTick), Is.True);
        Assert.That(Rules.TurnBreaksTail(-12f), Is.True);
    }

    /// <summary>
    /// Живая сцена: герой кружит вокруг босса (то быстрее его поворота, то медленнее, на 4,6 и 6 м). На каждом
    /// тике без действия, где корпус Sim повернулся быстрее порога, вид играет разворот или ход, и лапы
    /// переступают не реже поворота (90° на цикл/клип) — тело не вращается на стоящих лапах. Прежнее правило
    /// (Walk по пройденному пути, как только тело сдвинулось) в этой же сцене крутило тело почти на месте.
    /// </summary>
    [Test]
    public void LiveBoss_HeroCircles_TheBodyNeverSpinsOnStillLegs()
    {
        var sim = Arena();
        double angle = 180.0;
        float stride = Rules.DefaultWalkStride * 1.15f;
        int turnTicks = 0, creepTurns = 0, walkTurns = 0, oldSpins = 0, checkedTicks = 0;
        bool shownTurn = false;
        while (sim.Tick < 2400)
        {
            // Темп кружения: 3,5°/тик (быстрее поворота босса 2,5°) и 1,6°/тик (босс догоняет и ползёт); радиус 4,6 / 6 м.
            int block = sim.Tick / 240;
            double step = block % 2 == 0 ? 3.5 : 1.6;
            double radius = block % 4 < 2 ? 4.6 : 6.0;
            angle += step;
            var boss = sim.Entities.Position[Boss];
            double rad = angle * Math.PI / 180.0;
            sim.Entities.Position[0] = boss + new FixVec2(Fix64.FromDouble(radius * Math.Cos(rad)), Fix64.FromDouble(radius * Math.Sin(rad)));

            FixVec2 before = sim.Entities.Facing[Boss];
            Step(sim);
            if (!sim.ThicketMasterAwake(Boss) || sim.TryGetThicketMasterAction(Boss, out _)) { shownTurn = false; continue; }
            FixVec2 after = sim.Entities.Facing[Boss];
            float turn = SignedDegrees(before, after);
            float speed = sim.Entities.Velocity[Boss].Length.ToFloat() * Simulation.TicksPerSecond;
            bool turning = Math.Abs(turn) * Simulation.TicksPerSecond > Rules.TurnRateThreshold;
            var motion = Rules.Locomotion(speed, turning, shownTurn);
            shownTurn = motion == ThicketMotion.Turn;
            if (!turning) continue;
            checkedTicks++;
            Assert.That(motion, Is.Not.EqualTo(ThicketMotion.Idle), $"корпус крутится {turn:0.##}°/тик — не покой, тик {sim.Tick}");
            float legs = motion == ThicketMotion.Turn
                ? Math.Abs(turn) / Rules.TurnClipDegrees
                : Rules.WalkCyclesTurning(speed / Simulation.TicksPerSecond, turn, stride);
            Assert.That(legs, Is.GreaterThanOrEqualTo(Math.Abs(turn) / 90f - 1e-6f),
                $"лапы отстают от поворота: {motion}, {speed:0.##} м/с, {turn:0.##}°/тик, тик {sim.Tick}");
            if (motion == ThicketMotion.Turn) turnTicks++;
            if (motion == ThicketMotion.Turn && speed > Rules.WalkThresholdSpeed) creepTurns++;
            if (motion == ThicketMotion.Walk) walkTurns++;
            // Прежнее правило: ход по пути при любом сдвиге — лапы вчетверо медленнее поворота.
            if (speed > Rules.WalkThresholdSpeed && Rules.WalkCycles(speed / Simulation.TicksPerSecond, stride) < .25f * Math.Abs(turn) / 90f)
                oldSpins++;
        }
        TestContext.WriteLine($"тиков поворота {checkedTicks}: разворот {turnTicks} (ползком {creepTurns}), ход {walkTurns}; " +
                              $"прежнее правило крутило бы тело почти на месте {oldSpins} тиков");
        Assert.That(turnTicks, Is.GreaterThan(30), "сцена не дошла до разворота на месте");
        Assert.That(creepTurns, Is.GreaterThan(0), "сцена не проверила «почти на месте»");
        Assert.That(oldSpins, Is.GreaterThan(0), "сцена не воспроизводит жалобу — прежнее правило здесь не крутило");
    }

    // ------------------------------------------------------------ шаги под ударом лапы (проверка находок 03.10)

    [Test]
    public void PawTurnLegs_Mask_HindLegsAndTheSupportingForepaw()
    {
        foreach (bool right in new[] { true, false })
        {
            foreach (string hind in new[] { "leg_hind_L_upper", "leg_hind_L_lower", "leg_hind_L_foot", "leg_hind_R_upper", "leg_hind_R_foot" })
                Assert.That(Rules.PawTurnMasks(hind, right), Is.True, hind);
            foreach (string trunk in new[] { "root", "hips", "spine_01", "chest", "head", "crown_L", "bush", "tail_03", "", null })
                Assert.That(Rules.PawTurnMasks(trunk, right), Is.False, "корпус — из клипа удара: " + trunk);
            string support = right ? "leg_front_L_" : "leg_front_R_", striking = right ? "leg_front_R_" : "leg_front_L_";
            foreach (string part in new[] { "upper", "lower", "paw", "toe" })
            {
                Assert.That(Rules.PawTurnMasks(support + part, right), Is.True, "опорная передняя переступает: " + support + part);
                Assert.That(Rules.PawTurnMasks(striking + part, right), Is.False, "бьющая лапа — из клипа удара: " + striking + part);
            }
        }
        Assert.That(Rules.PawTurnLayer(true), Is.Not.EqualTo(Rules.PawTurnLayer(false)));
        Assert.That(Rules.PawTurnParameter(true, ThicketClip.TurnL), Is.EqualTo("LegsRTurnLPhase"));
        Assert.That(Rules.PawTurnParameter(false, ThicketClip.TurnR), Is.EqualTo("LegsLTurnRPhase"));
    }

    [Test]
    public void PawTurnLegs_StepsWithTheTurn_FadesAfter_SwapsSidesWithoutLosingWeight()
    {
        const float dt = 1f / 30f, turn = 3.5f;
        var legs = new ThicketPawTurnLegs();
        legs.Step(0f, dt, paw: true, rightStrikes: false);
        Assert.That(legs.Weight, Is.EqualTo(0f), "серия без поворота — шагов нет");

        // Шаги с нуля — сразу слой бьющей лапы: опора — другая передняя, бьющую не трогают.
        var fresh = new ThicketPawTurnLegs { Right = 0f };
        fresh.Step(-turn, dt, paw: true, rightStrikes: true);
        Assert.That(fresh.Weight, Is.GreaterThan(0f));
        Assert.That(fresh.WeightLeft, Is.EqualTo(0f), "под ударом правой слой левой (бьющая правая в его маске) — 0");

        // Корпус крутится 3,5°/тик: вес за PawTurnInTicks, доля клипа = поворот / 90°.
        float travel = 0f;
        for (int t = 1; t <= 17; t++)
        {
            legs.Step(-turn, dt, paw: true, rightStrikes: true);
            travel += turn;
            Assert.That(legs.Clip, Is.EqualTo(ThicketClip.TurnL));
            Assert.That(legs.Phase, Is.EqualTo(Rules.TurnPhase(travel)).Within(1e-4), "лапы не отстают от поворота, тик " + t);
            if (t >= Rules.PawTurnInTicks) Assert.That(legs.Weight, Is.EqualTo(1f).Within(1e-5), "тик " + t);
        }
        Assert.That(legs.WeightRight, Is.EqualTo(1f).Within(1e-5), "бьёт правая — слой правой");

        // Удар левой: слой меняется за PawTurnSwapTicks, сумма весов (задние лапы в обоих) — та же.
        for (int t = 0; t < 6; t++)
        {
            legs.Step(-turn, dt, paw: true, rightStrikes: false);
            Assert.That(legs.WeightRight + legs.WeightLeft, Is.EqualTo(legs.Weight).Within(1e-5));
        }
        Assert.That(legs.WeightLeft, Is.EqualTo(1f).Within(1e-5), "к удару левой — слой левой");

        // Смена стороны поворота — клип другой, счёт заново.
        legs.Step(turn, dt, paw: true, rightStrikes: false);
        Assert.That(legs.Clip, Is.EqualTo(ThicketClip.TurnR));
        Assert.That(legs.Phase, Is.EqualTo(Rules.TurnPhase(turn)).Within(1e-4));

        // Корпус встал, удар идёт: вес и доля стоят — стоящие лапы там, куда шагнули (не едут назад между ударами).
        float phase = legs.Phase;
        for (int t = 0; t < 12; t++)
        {
            legs.Step(0f, dt, paw: true, rightStrikes: false);
            Assert.That(legs.Weight, Is.EqualTo(1f).Within(1e-5), "удар без поворота — шаги держатся, тик " + t);
            Assert.That(legs.Phase, Is.EqualTo(phase), "доля стоит");
        }

        // Клип удара кончился (покой, ход, разворот на месте): шаги гаснут за PawTurnOutTicks; после — их нет.
        int fade = 0;
        while (legs.Weight > 0f)
        {
            legs.Step(0f, dt, paw: false, rightStrikes: false);
            Assert.That(++fade, Is.LessThan(30), "шаги гаснут");
        }
        Assert.That(fade, Is.LessThanOrEqualTo((int)Rules.PawTurnOutTicks + 1));
        Assert.That(legs.Sign, Is.EqualTo(0f));

        // Вне клипа удара (хвост, покой) — не включаются, пауза (dt 0) держит.
        legs.Step(-turn, dt, paw: false, rightStrikes: true);
        Assert.That(legs.Weight, Is.EqualTo(0f));
        legs.Step(-turn, dt, paw: true, rightStrikes: true);
        float held = legs.Weight;
        legs.Step(-turn, 0f, paw: true, rightStrikes: true);
        Assert.That(legs.Weight, Is.EqualTo(held), "пауза держит");
    }

    /// <summary>
    /// Живая сцена (баланс 02.10, ночь: корпус в серии лапы — 3,5°/тик, первый удар до 59,5°): герой то у бока
    /// босса, то у другого («подмышка»), серии лапы доворачивают корпус к нему. На каждом тике клипа удара, где
    /// корпус Sim повернулся быстрее порога, шаги разворота под ударом включены (вес 1 после проявления) и их
    /// клип идёт по повороту — тело не крутится на стоящих лапах клипа удара. Прежний вид (только PawR/PawL)
    /// крутил бы корпус на стоящих лапах все эти тики.
    /// </summary>
    [Test]
    public void LiveBoss_PawSeriesTurnsTheBody_LegsStepWithIt()
    {
        var sim = Arena();
        var legs = new ThicketPawTurnLegs();
        int turningTicks = 0, series = 0, lastSerial = 0;
        float maxSeriesTurn = 0f, seriesTurn = 0f, shownPhase = 0f, shownSign = 0f;
        bool wasTurning = false;
        while (sim.Tick < 2400)
        {
            // Раз в 2 с — герой у бока босса (75° от взгляда, 2,6 м), стороны по очереди.
            if (sim.Tick >= 180 && sim.Tick % 60 == 0)
            {
                var facing = sim.Entities.Facing[Boss].Normalized();
                double side = (sim.Tick / 60) % 2 == 0 ? 1 : -1, angle = side * 75.0 * Math.PI / 180.0;
                double fx = facing.X.ToFloat(), fy = facing.Y.ToFloat();
                double dx = fx * Math.Cos(angle) - fy * Math.Sin(angle), dy = fx * Math.Sin(angle) + fy * Math.Cos(angle);
                sim.Entities.Position[0] = sim.Entities.Position[Boss]
                    + new FixVec2(Fix64.FromDouble(2.6 * dx), Fix64.FromDouble(2.6 * dy));
            }
            FixVec2 before = sim.Entities.Facing[Boss];
            Step(sim);
            float tick = sim.Tick - 1;
            // Знак как у вида: SignedAngle вокруг +Y в мире (X, Z) — против знака угла в осях Sim (X, Y).
            float yaw = -SignedDegrees(before, sim.Entities.Facing[Boss]);
            bool paw = sim.TryGetThicketMasterAction(Boss, out var a) && a.Action == ThicketMasterAction.Paw;
            var pose = paw ? Rules.Action(a, tick) : default;
            bool pawClip = paw && (pose.Clip == ThicketClip.PawR || pose.Clip == ThicketClip.PawL);
            legs.Step(pawClip ? yaw : 0f, 1f / 30f, pawClip, pose.Clip == ThicketClip.PawR);
            if (paw && a.Serial != lastSerial) { lastSerial = a.Serial; series++; seriesTurn = 0f; }
            bool turning = pawClip && Math.Abs(yaw) * Simulation.TicksPerSecond > Rules.TurnRateThreshold;
            if (turning)
            {
                seriesTurn += Math.Abs(yaw);
                maxSeriesTurn = Math.Max(maxSeriesTurn, seriesTurn);
                if (wasTurning)
                {
                    // Второй тик поворота и дальше: шаги в полную силу, клип разворота сдвинулся на поворот / 90°
                    // (поворот сменил сторону — другой клип с начала, смесью в виде).
                    turningTicks++;
                    Assert.That(legs.Weight, Is.EqualTo(1f).Within(1e-5), $"корпус крутится {yaw:0.##}°/тик под ударом — лапы стоят, тик {tick}");
                    float step = legs.Sign == shownSign ? legs.Phase - shownPhase : legs.Phase;
                    if (step < 0f) step += 1f;
                    Assert.That(step, Is.EqualTo(Math.Abs(yaw) / Rules.TurnClipDegrees).Within(1e-3), $"шаги отстают от поворота, тик {tick}");
                }
                shownPhase = legs.Phase;
                shownSign = legs.Sign;
            }
            wasTurning = turning;
        }
        TestContext.WriteLine($"серий лапы {series}, тиков поворота под ударом {turningTicks}, наибольший поворот за серию {maxSeriesTurn:0.#}°");
        Assert.That(series, Is.GreaterThan(5), "серий лапы мало");
        Assert.That(turningTicks, Is.GreaterThan(40), "сцена не крутила корпус под ударом");
        Assert.That(maxSeriesTurn, Is.GreaterThan(40f), "первый удар серии доворачивает к герою у бока больше 40°");
    }

    // ------------------------------------------------------------ лапа

    /// <summary>
    /// Замахи серии по фазам и промежуткам (баланс 02.10, ночь, контракт § 11: первый 17 / 16 / 15, дальше — броски
    /// 9–12 / 8–11 / 8–10 в Tag): лента когтей начинается у верха замаха при любом из них. Знак шага, прочитанный
    /// позже, чем серия ушла дальше (догон кадра, съёмка), берёт замах и удар своего шага из полей серии
    /// (ThicketPawGapOf), а не фиксированные 17 / 10 и не удар текущего шага (проверка находок 03.10).
    /// </summary>
    [Test]
    public void PawWindups_ByPhaseAndGaps_ClawRibbonStartsOnTheSameClipFrame()
    {
        Assert.That(Rules.ClawLeadTicks(15), Is.EqualTo(6), "первый удар фазы 3");
        Assert.That(Rules.ClawLeadTicks(16), Is.EqualTo(6), "первый удар фазы 2");
        Assert.That(Rules.ClawLeadTicks(17), Is.EqualTo(7), "первый удар фазы 1");
        for (int gap = 8; gap <= 12; gap++)
            Assert.That(Rules.ClawLeadTicks(gap - Simulation.ThicketPawStrikeTicks), Is.InRange(3, 4), "следующий удар, промежуток " + gap);
        Assert.That(Rules.ClawLeadTicks(Simulation.ThicketPawHeavyWindupTicks), Is.EqualTo(12));
        Assert.That(Rules.ClawLeadTicks(1), Is.EqualTo(2), "не короче двух тиков");

        // Серия с тика 100: первый замах 17, промежутки 11 и 8 (Tag) — удары 117 / 128 / 136; фаза 3: 15, 9 и 10;
        // тяжёлый первый замах 30.
        var first = Paw(100, 117, 136, 0, 3, gap1: 11, gap2: 8);
        var late = Paw(100, 115, 134, 0, 3, gap1: 9, gap2: 10);
        var heavy = Paw(100, 130, 141, 0, 2, gap1: 11, gap2: 9);
        Assert.That(Rules.PawWindupTicks(Stage(first, 1)), Is.EqualTo(10), "промежуток 11 минус тик контакта прошлой лапы");
        Assert.That(Rules.PawWindupTicks(Stage(first, 2)), Is.EqualTo(7));
        foreach (var a in new[] { first, Stage(first, 1), Stage(first, 2), late, Stage(late, 1), Stage(late, 2), heavy, Stage(heavy, 1) })
        {
            int windup = Rules.PawWindupTicks(a);
            int lead = Rules.ClawLeadTicks(windup);
            Assert.That(a.ImpactTick - lead, Is.GreaterThan(a.StageStartTick), $"лента — внутри замаха шага {a.Stage}");
            var pose = Rules.Action(a, a.ImpactTick - lead);
            Assert.That(pose.Clip, Is.EqualTo(Rules.PawClip(a.Stage)));
            Assert.That(pose.Frame, Is.InRange(12.5f, 16f), $"лента начинается у верха замаха, шаг {a.Stage}, замах {windup}");
            Assert.That(Rules.Action(a, a.ImpactTick).Frame, Is.EqualTo((float)Rules.PawContactFrame), "контакт — на тике удара");

            // Знак любого шага, прочитанный на этом шаге серии: тот же замах и тот же удар, что у самого шага.
            for (int stage = 0; stage < a.Stages; stage++)
            {
                var own = Stage(a, stage);
                Assert.That(Rules.PawWindupTicks(a, stage), Is.EqualTo(Rules.PawWindupTicks(own)), $"замах шага {stage}, прочитан на шаге {a.Stage}");
                Assert.That(Rules.PawImpactTick(a, stage), Is.EqualTo(own.ImpactTick), $"удар шага {stage}, прочитан на шаге {a.Stage}");
            }
        }
    }

    /// <summary>
    /// Живой босс: каждый удар серии (знак Started(Paw, k)) открывает в этот же тик метку-сектор SharedView из
    /// бьющего плеча, без SafeZone — её рисует общий GroundTelegraphView; удар метки = удар шага, фигура —
    /// ThicketPawStrikeSector, раствор 100°, радиус 3,58. Упреждение ленты когтей лежит внутри замаха.
    /// </summary>
    [Test]
    public void LiveBoss_EveryPawStrikeOpensASharedSectorFromTheStrikingShoulder()
    {
        var sim = Arena();
        HeroInFront(sim, 2.6);
        int strikes = 0, lefts = 0;
        while (sim.Tick < 1500)
        {
            Step(sim);
            int tick = sim.Tick - 1;
            foreach (var e in sim.Events)
            {
                if (e.Type != SimEventType.EnemyActionStarted || e.Source != Boss || e.ActionVariant != (int)EnemyActionKind.ThicketPaw)
                    continue;
                Assert.That(sim.TryGetThicketMasterAction(Boss, out var a) && a.Action == ThicketMasterAction.Paw, Is.True);
                Assert.That(a.Stage, Is.EqualTo(e.Amount));
                Assert.That(a.StageStartTick, Is.EqualTo(tick));
                Assert.That(a.TelegraphSerial, Is.Not.EqualTo(0), "у удара есть метка");
                bool found = false;
                for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                {
                    if (!sim.TryGetTelegraph(slot, out var t) || t.Serial != a.TelegraphSerial) continue;
                    found = true;
                    Assert.That(t.SharedView && (t.Flags & TelegraphFlags.SafeZone) == 0, Is.True, "общий вид рисует метку");
                    Assert.That(t.Source, Is.EqualTo(Boss));
                    Assert.That(t.Shape, Is.EqualTo(TelegraphShape.Sector));
                    Assert.That(t.StartTick, Is.EqualTo(tick), "метка — от знака удара");
                    Assert.That(t.ImpactTick, Is.EqualTo(a.ImpactTick), "заливка доходит ровно к удару");
                    var shoulder = Simulation.ThicketPawShoulder(a.Origin, a.Direction, a.Stage);
                    Assert.That((t.Origin - shoulder).Length.ToFloat(), Is.LessThan(1e-3f), "вершина — бьющее плечо");
                    Assert.That(t.Radius.ToFloat(), Is.EqualTo(Simulation.ThicketPawReach.ToFloat()).Within(1e-3));
                    Assert.That(2.0 * Math.Acos(t.ArcCos.ToFloat()) * 180.0 / Math.PI, Is.EqualTo(100.0).Within(.1), "раствор 100°");
                    // Правая лапа — плечо справа от взгляда удара, левая — слева.
                    float side = Cross(a.Direction, t.Origin - a.Origin);
                    if (Rules.PawIsRight(a.Stage)) Assert.That(side, Is.LessThan(0f), "правое плечо");
                    else Assert.That(side, Is.GreaterThan(0f), "левое плечо");
                }
                Assert.That(found, Is.True, "метка удара в списке Sim");
                Assert.That(a.ImpactTick - Rules.ClawLeadTicks(Rules.PawWindupTicks(a)), Is.GreaterThan(tick), "лента — после знака");
                strikes++;
                if (!Rules.PawIsRight(a.Stage)) lefts++;
            }
        }
        TestContext.WriteLine($"ударов лапы {strikes}, левых {lefts}");
        Assert.That(strikes, Is.GreaterThan(3));
        Assert.That(lefts, Is.GreaterThan(0), "левая лапа серии не проверена");
    }

    // ------------------------------------------------------------ кат-сцена: HUD

    /// <summary>
    /// HUD и миникарта: холст со своей группой кат-сцены гаснет и выключается, холст с чужой группой (боевой HUD —
    /// группа PlayerHud) выключается на середине затухания; оба выключены всё окно до E и включаются к концу
    /// возврата HUD, один раз, без мигания.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void IntroHud_CanvasesOffForTheWholeHold_BackAfterTheEnd(bool fades)
    {
        const int S = 300, W = S + Simulation.ThicketIntroLeadTicks, E = S + Simulation.ThicketIntroTicks;
        Assert.That(Intro.HudCanvasOn(Intro.LookAt(S, W, E, E, S).Hud, fades), Is.True, "в тик S HUD ещё на месте");
        for (float t = S + Intro.HudOutTicks; t <= E; t += .25f)
            Assert.That(Intro.HudCanvasOn(Intro.LookAt(S, W, E, E, t).Hud, fades), Is.False, $"холст HUD выключен, тик {t}");
        Assert.That(Intro.HudCanvasOn(Intro.LookAt(S, W, E, E, E + Intro.HudInTicks).Hud, fades), Is.True, "HUD вернулся");
        Assert.That(Intro.HudCanvasOn(Intro.Look.Finished.Hud, fades), Is.True);

        int switches = 0;
        bool on = true;
        for (float t = S - 2; t <= Intro.DoneTick(E, E) + 2; t += .05f)
        {
            bool now = Intro.HudCanvasOn(Intro.LookAt(S, W, E, E, t).Hud, fades);
            if (now != on) switches++;
            on = now;
        }
        Assert.That(switches, Is.EqualTo(2), "выключился и включился по разу");

        // Босс умер в окне — HUD возвращается от тика смерти.
        int death = W + 10;
        Assert.That(Intro.HudCanvasOn(Intro.LookAt(S, W, E, death, death + Intro.HudInTicks).Hud, fades), Is.True);
    }

    // ------------------------------------------------------------ помощники

    private static ThicketMasterState Paw(int start, int impact, int last, int stage, int stages, int gap1 = 0, int gap2 = 0)
        => new ThicketMasterState
        {
            Serial = 1, Action = ThicketMasterAction.Paw, StartTick = start, StageStartTick = start, ImpactTick = impact,
            LastImpactTick = last, EndTick = last + 1, Stage = stage, Stages = stages, Tag = gap1 | (gap2 << 8),
        };

    /// <summary>
    /// Шаг stage серии так, как его ставит Sim (из шага 0 той же серии или любого её шага): знак в тик прошлого удара,
    /// удар через промежуток серии (Simulation.ThicketPawGapOf: Tag, 0 — средние 10).
    /// </summary>
    private static ThicketMasterState Stage(ThicketMasterState any, int stage)
    {
        var a = any;
        int impact = any.ImpactTick;
        for (int k = any.Stage - 1; k >= 0; k--) impact -= Simulation.ThicketPawGapOf(any, k);
        a.Stage = 0;
        a.StageStartTick = any.StartTick;
        a.ImpactTick = impact;
        for (int k = 1; k <= stage; k++)
        {
            a.Stage = k;
            a.StageStartTick = a.ImpactTick;
            a.ImpactTick += Simulation.ThicketPawGapOf(any, k - 1);
        }
        return a;
    }

    private static float SignedDegrees(FixVec2 from, FixVec2 to)
    {
        double ax = from.X.ToFloat(), ay = from.Y.ToFloat(), bx = to.X.ToFloat(), by = to.Y.ToFloat();
        return (float)(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by) * 180.0 / Math.PI);
    }

    /// <summary>Векторное произведение в осях Sim: &gt; 0 — b слева от a.</summary>
    private static float Cross(FixVec2 a, FixVec2 b) => a.X.ToFloat() * b.Y.ToFloat() - a.Y.ToFloat() * b.X.ToFloat();

    private static Simulation Arena()
    {
        var sim = new Simulation(77, 64);
        sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(6));
        var e = sim.Entities;
        e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(100000));
        e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
        e.RefreshStats(0);
        e.Health[0] = e.MaxHealth[0];
        return sim;
    }

    private static void HeroInFront(Simulation sim, double distance)
    {
        var boss = sim.Entities.Position[Boss];
        sim.Entities.Position[0] = boss + new FixVec2(-Fix64.FromDouble(distance), Fix64.Zero);
    }

    private static void Step(Simulation sim)
    {
        sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
        sim.Step(InputFrame.Empty);
    }
}
