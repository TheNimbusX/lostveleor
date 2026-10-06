using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Шквал v2 (02.10): кадр клипа по тикам полёта — как timing.json, клипы и стыки — как в
// Blender, корень крутится вокруг левой лодыжки, лента каста из событий живой Sim:
// контакт кадра 6 в тик удара, взгляд на удар — по прыжку, в конце — взгляд Sim, без скачков.
public sealed class SquallClipRulesTests
{
    private static JsonElement Timing()
    {
        string path = Path.Combine(RepoRoot.Path, "ART", "characters", "pelag", "squall-forms-2026-10-02", "animation", "timing.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static PelagSquallClip ByName(string name)
    {
        foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
            if (PelagSquallClipRules.ClipName(clip) == name) return clip;
        return PelagSquallClip.None;
    }

    [Test]
    public void FlightFrameMatchesTimingJsonRetime()
    {
        int tables = 0;
        foreach (JsonProperty table in Timing().GetProperty("flight_retime").EnumerateObject())
        {
            int flight = int.Parse(table.Name);
            int k = 0;
            foreach (JsonElement frame in table.Value.EnumerateArray())
            {
                Assert.That(PelagSquallClipRules.FlightFrame(flight, k), Is.EqualTo(frame.GetSingle()).Within(.002f), $"F={flight} k={k}");
                k++;
            }
            Assert.That(k, Is.EqualTo(flight + 1), "F=" + flight);
            tables++;
        }
        Assert.That(tables, Is.EqualTo(Simulation.SquallMaxFlightTicks - Simulation.SquallMinFlightTicks + 1));
    }

    [Test]
    public void ClipSegmentsMatchSimTimings()
    {
        Assert.That(PelagSquallClipRules.LoadTicks, Is.EqualTo(Simulation.SquallWindupTicks));
        Assert.That(PelagSquallClipRules.SupportTicks, Is.EqualTo(Simulation.SquallStopTicks));
        Assert.That(PelagSquallClipRules.FinishTicks, Is.EqualTo(Simulation.SquallFinalHoldTicks + Simulation.SquallExitTicks));
        JsonElement clips = Timing().GetProperty("clips");
        foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
        {
            JsonElement entry = clips.GetProperty(PelagSquallClipRules.ClipName(clip));
            Assert.That(entry.GetProperty("frames").GetInt32() - 1, Is.EqualTo(PelagSquallClipRules.LastFrame(clip)), clip.ToString());
            Assert.That(entry.GetProperty("ticks").GetInt32(), Is.EqualTo(PelagSquallClipRules.LastFrame(clip)), clip.ToString());
        }
    }

    [Test]
    public void FlightFrameRisesToContactForAnyLength()
    {
        for (int flight = 2; flight <= 14; flight++)
        {
            float last = -1f;
            for (float k = 0f; k <= flight + .001f; k += .1f)
            {
                float frame = PelagSquallClipRules.FlightFrame(flight, k);
                Assert.That(frame, Is.GreaterThanOrEqualTo(last - 1e-4f), $"F={flight} k={k}");
                last = frame;
            }
            Assert.That(PelagSquallClipRules.FlightFrame(flight, flight), Is.EqualTo(PelagSquallClipRules.ContactFrame));
            Assert.That(PelagSquallClipRules.FlightFrame(flight, 1), Is.EqualTo(flight == 2 ? 3f : 1f), "тик 1 — толчок");
        }
    }

    [Test]
    public void SeamsAreTheTimingJsonZeroDegreeSeams()
    {
        int seams = 0;
        foreach (JsonElement seam in Timing().GetProperty("limits").GetProperty("seam_max_bone_deg").GetProperty("seams").EnumerateArray())
        {
            PelagSquallClip from = ByName(seam.GetProperty("from_clip").GetString());
            PelagSquallClip to = ByName(seam.GetProperty("to_clip").GetString());
            if (from == PelagSquallClip.None || to == PelagSquallClip.None) continue;
            Assert.That(seam.GetProperty("max_bone_deg").GetSingle(), Is.LessThanOrEqualTo(.5f), $"{from}->{to}");
            Assert.That(PelagSquallClipRules.IsSeam(from, to), Is.True, $"{from}->{to}");
            seams++;
        }
        Assert.That(seams, Is.EqualTo(7));
        Assert.That(PelagSquallClipRules.IsSeam(PelagSquallClip.Forehand, PelagSquallClip.FinishBack), Is.False);
        Assert.That(PelagSquallClipRules.IsSeam(PelagSquallClip.Backhand, PelagSquallClip.ReturnFore), Is.False);
    }

    [Test]
    public void LeftAnkleStandsWhereTheRootPivots()
    {
        JsonElement rows = Timing().GetProperty("rows");
        void Check(string clip, int from, int to)
        {
            for (int frame = from; frame <= to; frame++)
            {
                JsonElement ankle = rows.GetProperty(clip)[frame].GetProperty("Left").GetProperty("ankle");
                Assert.That(ankle[0].GetSingle(), Is.EqualTo(PelagSquallClipRules.AuthoredAnkleLeft).Within(.002f), $"{clip} {frame}");
                Assert.That(-ankle[1].GetSingle(), Is.EqualTo(PelagSquallClipRules.AuthoredAnkleForward).Within(.002f), $"{clip} {frame}");
            }
        }
        Check("Pelag_AN_Squall2_Load", 0, 2);
        Check("Pelag_AN_Squall2_Forehand", 6, 8);
        Check("Pelag_AN_Squall2_Backhand", 6, 8);
        Check("Pelag_AN_Squall2_FinishFore", 0, 9);
        Check("Pelag_AN_Squall2_FinishBack", 0, 9);
        Check("Pelag_AN_Squall2_ReturnFore", 6, 10);
    }

    [Test]
    public void PivotShiftKeepsTheAnkleInPlace()
    {
        var random = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            float yaw0 = (float)(random.NextDouble() * 360 - 180), yaw1 = yaw0 + (float)(random.NextDouble() * 240 - 120);
            PelagSquallClipRules.LeftAnkle(yaw0, 1.01f, out float ax, out float ay);
            PelagSquallClipRules.PivotShift(yaw0, yaw1, 1.01f, out float sx, out float sy);
            PelagSquallClipRules.LeftAnkle(yaw1, 1.01f, out float bx, out float by);
            Assert.That(sx + bx, Is.EqualTo(ax).Within(1e-4f));
            Assert.That(sy + by, Is.EqualTo(ay).Within(1e-4f));
        }
        PelagSquallClipRules.LeftAnkle(90f, 1f, out float x, out float y);
        Assert.That(x, Is.EqualTo(-PelagSquallClipRules.LeftAnkleLeft).Within(1e-4f), "взгляд +Y: левая сторона — минус X");
        Assert.That(y, Is.EqualTo(PelagSquallClipRules.LeftAnkleForward).Within(1e-4f));
    }

    [Test]
    public void TurnsFollowTheSlashNearHalfTurnAndRespectTheSpeedCap()
    {
        Assert.That(PelagSquallClipRules.SignedTurn(0f, -175f, 1), Is.EqualTo(185f).Within(1e-3f), "после прямого — влево");
        Assert.That(PelagSquallClipRules.SignedTurn(0f, -175f, -1), Is.EqualTo(-175f).Within(1e-3f));
        Assert.That(PelagSquallClipRules.SignedTurn(170f, -170f, 0), Is.EqualTo(20f).Within(1e-3f));
        for (float delta = 5f; delta <= 80f; delta += 5f)
        {
            float ticks = PelagSquallClipRules.TurnTicks(delta, PelagSquallClipRules.TurnTicksBase, 6f);
            Assert.That(1.5f * delta / ticks, Is.LessThanOrEqualTo(PelagSquallClipRules.MaxTurnDegPerTick + 1e-3f), "Δ=" + delta);
        }
    }

    [Test]
    public void ReturnLegTicksMatchTheSimFlightRule()
    {
        for (int mm = 300; mm <= 3900; mm += 50)
        {
            FixVec2 to = new FixVec2(Fix64.Ratio(mm, 1000), Fix64.Zero);
            Assert.That(PelagSquallClipRules.ReturnLegTicks(FixVec2.Zero, to),
                Is.EqualTo(Simulation.SquallFlightTicks(FixVec2.Distance(FixVec2.Zero, to))), mm + " мм");
        }
    }

    [Test]
    public void TimelineAlternatesClipsAndHitsFrameSixOnTheStrikeTick()
    {
        var line = new PelagSquallTimeline();
        line.Begin(1, 100, 0f, 0f, 0f);
        line.SetStartYaw(30f, 99f);
        line.Jump(102, 0, 3, false, 2f, 0f, 0f, 100.5f);
        line.Strike(105, 0, 2f, 0f, PelagSquallNext.Jump, 60f, 104.5f);
        line.Jump(107, 1, 2, true, 2f + .66f, 1.143f, 60f, 106.5f);
        line.Strike(109, 1, 2.66f, 1.143f, PelagSquallNext.Finish, 60f, 108.5f);
        line.End(118, SquallEnd.Done);

        Assert.That(line.Sample(101f, 1f).Clip, Is.EqualTo(PelagSquallClip.Load));
        Assert.That(line.Sample(101f, 1f).Frame, Is.EqualTo(1f).Within(1e-4f));
        AssertPose(line.Sample(103f, 1f), PelagSquallClip.Forehand, 1f);
        AssertPose(line.Sample(104f, 1f), PelagSquallClip.Forehand, 3.5f);
        AssertPose(line.Sample(105f, 1f), PelagSquallClip.Forehand, 6f);
        AssertPose(line.Sample(106f, 1f), PelagSquallClip.Forehand, 7f);
        AssertPose(line.Sample(107f, 1f), PelagSquallClip.Backhand, 0f);
        AssertPose(line.Sample(108f, 1f), PelagSquallClip.Backhand, 3f);
        AssertPose(line.Sample(109f, 1f), PelagSquallClip.Backhand, 6f);
        AssertPose(line.Sample(109.5f, 1f), PelagSquallClip.FinishBack, .5f);
        AssertPose(line.Sample(113f, 1f), PelagSquallClip.FinishBack, 4f);
        Assert.That(line.Sample(118f, 1f).Finished, Is.True);
        Assert.That(line.Sample(117.9f, 1f).Finished, Is.False);

        Assert.That(line.Sample(105f, 1f).Yaw, Is.EqualTo(0f).Within(.01f), "удар — по прыжку");
        Assert.That(line.Sample(106f, 1f).Yaw, Is.EqualTo(0f).Within(.01f), "кадр 6→7 — без поворота корня");
        Assert.That(line.Sample(109f, 1f).Yaw, Is.EqualTo(60f).Within(.1f), "к удару повёрнут на прыжок");
        Assert.That(line.Sample(102f, 1f).Yaw, Is.LessThan(30f).And.GreaterThan(0f), "замах доворачивает с прежнего взгляда");
    }

    [Test]
    public void TimelineLateFinishCrossfadesFromFrameTwo()
    {
        var line = new PelagSquallTimeline();
        line.Begin(1, 0, 0f, 0f, 0f);
        line.Jump(2, 0, 2, false, 1.3f, 0f, 0f, .5f);
        line.Strike(4, 0, 1.3f, 0f, PelagSquallNext.Jump, 45f, 3.5f);
        Assert.That(line.AwaitingContinuation(out int strike), Is.True);
        Assert.That(strike, Is.EqualTo(4));
        line.LateFinish(6f);
        Assert.That(line.AwaitingContinuation(out _), Is.False);
        AssertPose(line.Sample(5.5f, 1f), PelagSquallClip.Forehand, 7.5f);
        PelagSquallPose late = line.Sample(6f, 1f);
        AssertPose(late, PelagSquallClip.FinishFore, PelagSquallClipRules.LateFinishFrame);
        Assert.That(late.Crossfade, Is.True);
        Assert.That(line.Sample(13f, 1f).Finished, Is.True);
    }

    // Стойка покоя на входе и выходе (проверка 02.10: левая стопа ехала по земле 24 см).

    [Test]
    public void PlantedAnkleHeightSplitsStandingFromLiftedFeet()
    {
        int standing = 0, lifted = 0;
        foreach (JsonProperty clip in Timing().GetProperty("rows").EnumerateObject())
            foreach (JsonElement row in clip.Value.EnumerateArray())
                foreach (string side in new[] { "Left", "Right" })
                {
                    float z = row.GetProperty(side).GetProperty("ankle")[2].GetSingle();
                    if (z <= .14f) { Assert.That(z, Is.LessThan(PelagSquallClipRules.PlantedAnkleHeight - .02f), clip.Name); standing++; }
                    else { Assert.That(z, Is.GreaterThan(PelagSquallClipRules.PlantedAnkleHeight), clip.Name); lifted++; }
                }
        Assert.That(standing, Is.GreaterThan(40));
        Assert.That(lifted, Is.GreaterThan(30));
    }

    [Test]
    public void ExitStepLeavesAndLandsAtRestAndLiftsInTheMiddle()
    {
        PelagSquallClipRules.ExitStep(0f, out float travel, out float lift);
        Assert.That(travel, Is.EqualTo(0f).Within(1e-5f), "старт — там, где стояла стопа");
        Assert.That(lift, Is.EqualTo(0f).Within(1e-5f));
        PelagSquallClipRules.ExitStep(1f, out travel, out lift);
        Assert.That(travel, Is.EqualTo(1f).Within(1e-5f), "конец — поза смешивания, без скачка при снятии IK");
        Assert.That(lift, Is.EqualTo(0f).Within(1e-5f));
        PelagSquallClipRules.ExitStep(.5f, out travel, out lift);
        Assert.That(travel, Is.EqualTo(.5f).Within(1e-5f));
        Assert.That(lift, Is.EqualTo(1f).Within(1e-5f));
        PelagSquallClipRules.ExitStep(-1f, out travel, out lift);
        Assert.That(travel + lift, Is.EqualTo(0f).Within(1e-5f));
        PelagSquallClipRules.ExitStep(2f, out travel, out lift);
        Assert.That(travel, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(lift, Is.EqualTo(0f).Within(1e-5f));

        // У земли стопа почти не едет: пока подъём меньше трети, пройдено меньше трети пути.
        float last = 0f;
        for (float u = 0f; u <= 1.0001f; u += .05f)
        {
            PelagSquallClipRules.ExitStep(u, out travel, out lift);
            Assert.That(travel, Is.GreaterThanOrEqualTo(last - 1e-5f), "u=" + u);
            Assert.That(lift, Is.GreaterThanOrEqualTo(0f));
            if (lift < 1f / 3f) Assert.That(Math.Min(travel, 1f - travel), Is.LessThan(1f / 3f), "u=" + u);
            last = travel;
        }
        Assert.That(PelagSquallClipRules.ExitStepLift, Is.InRange(.03f, .09f), "как шаги разворота на месте");
    }

    [Test]
    public void EntryWeightHoldsThroughTheWindupAndFadesInTheFirstFlight()
    {
        var idle = new PelagSquallTimeline();
        Assert.That(idle.EntryWeight(5f), Is.EqualTo(0f), "без каста сдвига нет");

        var line = new PelagSquallTimeline();
        line.Begin(1, 100, 0f, 0f, 0f);
        line.SetStartYaw(30f, 99f);
        line.Jump(102, 0, 3, false, 2f, 0f, 0f, 100.5f);
        line.Strike(105, 0, 2f, 0f, PelagSquallNext.Jump, 60f, 104.5f);
        line.Jump(107, 1, 2, true, 2.66f, 1.143f, 60f, 106.5f);

        Assert.That(line.EntryWeight(99.5f), Is.EqualTo(1f), "до каста на ленте — замах");
        Assert.That(line.EntryWeight(100f), Is.EqualTo(1f));
        Assert.That(line.EntryWeight(101.9f), Is.EqualTo(1f), "весь замах стопа стоит");
        Assert.That(line.EntryWeight(102f), Is.EqualTo(1f).Within(1e-5f), "старт полёта — без скачка");
        float last = 1f;
        for (float t = 102f; t <= 105f; t += .25f)
        {
            float w = line.EntryWeight(t);
            Assert.That(w, Is.EqualTo(PelagSquallClipRules.ShiftDecay(t - 102f, 3f)).Within(1e-5f), "как сдвиг опоры, t=" + t);
            Assert.That(w, Is.LessThanOrEqualTo(last + 1e-5f), "t=" + t);
            last = w;
        }
        Assert.That(line.EntryWeight(105f), Is.EqualTo(0f).Within(1e-5f), "к удару тело в точке Sim");
        Assert.That(line.EntryWeight(106f), Is.EqualTo(0f), "опора первого удара");
        Assert.That(line.EntryWeight(108f), Is.EqualTo(0f), "второй прыжок");
        line.Stop();
        Assert.That(line.EntryWeight(101f), Is.EqualTo(0f), "после показа — нет");
    }

    // Раунд 2 (проба 02.10, кадры 90–92): на касте таз +15 см и назад −17 см — смешивание
    // аниматора шло от голого CombatIdle (слои стойки сброшены сразу). Поза каста → поза клипа.
    // Раунд 3 (проверка 02.10, кадры 91–92): остаток — таз ±3 см, правая стопа 5 см: первый кадр
    // брал 19 % позы аниматора, пока тот ещё смешивал CombatIdle. Снимок держится до конца
    // смешивания, потом S-кривая — к первому полёту целиком клип.
    // Раунд 4 (проверка 03.10): при 30 к/с поза менялась целиком за кадр (шаг 1,0), при 40 —
    // 0,71. Переход знает длину кадра: кадр до конца смешивания и кадр после старта полёта.

    private static readonly int[] EntryFps = { 30, 40, 60, 120, 144 };

    /// <summary>Вес позы на кадре <paramref name="t"/> с от каста, первый полёт через <paramref name="flight"/> с, <paramref name="fps"/> к/с.</summary>
    private static float EntryPose(float t, float flight, int fps) => PelagSquallClipRules.EntryPoseWeight(t, flight - t, 1f / fps);

    /// <summary>Вес раунда 2 (S-кривая за .06 с от кадра каста) — для сравнения шага за кадр.</summary>
    private static float Round2EntryPose(float t) => PelagSquallClipRules.Smooth(t / .06f);

    /// <summary>Наибольший шаг веса раунда 2 за кадр при <paramref name="fps"/> к/с.</summary>
    private static float Round2Step(int fps)
    {
        float prev = 0f, step = 0f;
        for (int frame = 1; frame <= fps; frame++)
        {
            float w = Round2EntryPose(frame / (float)fps);
            step = Math.Max(step, w - prev);
            prev = w;
        }
        return step;
    }

    /// <summary>Потолок шага за кадр: 30–40 к/с — половина позы, 60 к/с и выше — не больше раунда 2.</summary>
    private static float EntryStepCap(int fps) => fps < 60 ? .5f : Round2Step(fps);

    [Test]
    public void EntryPoseStartsAtTheShownStanceAndIsTheClipInTheFirstFlight()
    {
        float hold = PelagSquallClipRules.EntryCrossFadeSeconds;
        float lead = PelagSquallClipRules.EntryLeadSeconds, tail = PelagSquallClipRules.EntryTailSeconds;
        Assert.That(hold, Is.EqualTo(.035f).Within(1e-6f), "смешивание аниматора на касте (SquallEnterBlend)");
        // Первый полёт — конец замаха (CastTick + LoadTicks) на тике показа; каст виден на CastTick − 1 + Alpha.
        Assert.That(PelagSquallClipRules.EntryFlightSeconds(100, 99f), Is.EqualTo(3f / 30f).Within(1e-6f));
        Assert.That(PelagSquallClipRules.EntryFlightSeconds(100, 99.5f), Is.EqualTo(2.5f / 30f).Within(1e-6f));
        Assert.That(PelagSquallClipRules.EntryFlightSeconds(100, 102f), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(PelagSquallClipRules.EntryPoseSeconds, Is.EqualTo(PelagSquallClipRules.LoadTicks / 30f).Within(1e-6f));
        Assert.That(PelagSquallClipRules.EntryPoseSeconds, Is.GreaterThan(hold), "окно перехода есть при любой Alpha");
        Assert.That(lead, Is.LessThan(hold), "кадр каста — снимок стойки");
        Assert.That(tail, Is.LessThan(1f / 30f), "переход кончается в первом тике полёта");

        Assert.That(PelagSquallClipRules.EntryPoseWeight(-1f, .1f, 1f / 60f), Is.EqualTo(0f));
        Assert.That(PelagSquallClipRules.EntryPoseWeight(0f, 0f, 1f / 60f), Is.EqualTo(1f), "полёт уже идёт — стойку не держим");
        Assert.That(PelagSquallClipRules.EntryPoseWeight(0f, 0f, 1f / 30f), Is.EqualTo(1f), "полёт уже идёт — стойку не держим");

        foreach (int fps in EntryFps)
        {
            float frame = 1f / fps;
            float start = Math.Max(0f, hold - Math.Max(frame, lead)), end = Math.Max(frame, tail);
            for (int phase = 0; phase < 100; phase++)
            {
                float alpha = phase / 100f, flight = (3f - alpha) / 30f;
                string at = $"{fps} к/с, alpha={alpha}";
                // До начала перехода — ровно та стойка, что видно; его начало и конец — без рывка.
                for (float t = 0f; t <= start + 1e-6f; t += 1f / 960f)
                    Assert.That(EntryPose(t, flight, fps), Is.EqualTo(0f), at + " t=" + t);
                Assert.That(EntryPose(start + 1f / 960f, flight, fps), Is.LessThan(.05f), "S-кривая: старт без рывка, " + at);
                Assert.That(EntryPose(flight + end - 1f / 960f, flight, fps), Is.GreaterThan(.98f), "S-кривая: к концу без рывка, " + at);
                // Через кадр после старта полёта и дальше — только клип.
                Assert.That(EntryPose(flight + end, flight, fps), Is.EqualTo(1f), at);
                Assert.That(EntryPose(flight + .5f, flight, fps), Is.EqualTo(1f), at);

                float last = 0f;
                for (float t = 0f; t <= flight + end + 1e-4f; t += 1f / 960f)
                {
                    float w = EntryPose(t, flight, fps);
                    Assert.That(w, Is.GreaterThanOrEqualTo(last - 1e-6f), "без возврата назад, " + at + " t=" + t);
                    // S-кривая на окне не короче 0,062 с: наклон до 24 в секунду.
                    Assert.That(w - last, Is.LessThan(.05f), "без скачка за 1/960 с, " + at + " t=" + t);
                    last = w;
                }
            }
        }
    }

    /// <summary>
    /// Кадры от каста при 30/40/60/120/144 к/с и любой Alpha каста (полёт через 2–3 тика): шаг
    /// позы за кадр — не больше половины при 30–40 к/с и не больше раунда 2 при 60+ (раунд 4:
    /// было 1,0 и 0,71); доля голого CombatIdle в кадре — не больше ~2,5 % (раунд 3: ~10 % — таз 3 см);
    /// в первом кадре полёта стойки не больше 30 %, через кадр (не меньше EntryTailSeconds) — только клип.
    /// </summary>
    [Test]
    public void EntryPoseStepPerFrameAtAnyFrameRateAndPhase()
    {
        float hold = PelagSquallClipRules.EntryCrossFadeSeconds;
        foreach (int fps in EntryFps)
        {
            float frame = 1f / fps, cap = EntryStepCap(fps), worst = 0f, worstShare = 0f;
            float end = Math.Max(frame, PelagSquallClipRules.EntryTailSeconds);
            for (int phase = 0; phase < 100; phase++)
            {
                float alpha = phase / 100f, flight = (3f - alpha) / 30f;
                string at = $"{fps} к/с, alpha={alpha}";
                Assert.That(EntryPose(0f, flight, fps), Is.EqualTo(0f), "кадр каста — снимок стойки, " + at);
                float prev = 0f;
                bool inFlight = false;
                for (int k = 1; k <= fps; k++)
                {
                    float t = k * frame, w = EntryPose(t, flight, fps);
                    Assert.That(w, Is.GreaterThanOrEqualTo(prev - 1e-6f), "без возврата назад, " + at + " кадр +" + k);
                    worst = Math.Max(worst, w - prev);
                    Assert.That(w - prev, Is.LessThanOrEqualTo(cap + 1e-5f), "шаг за кадр, " + at + " кадр +" + k);
                    float share = w * Math.Max(0f, 1f - t / hold);
                    worstShare = Math.Max(worstShare, share);
                    Assert.That(share, Is.LessThanOrEqualTo(.026f), "доля голого CombatIdle, " + at + " кадр +" + k);
                    if (!inFlight && t >= flight)
                    {
                        inFlight = true;
                        Assert.That(w, Is.GreaterThanOrEqualTo(.7f), "первый кадр полёта — в основном клип, " + at);
                    }
                    if (t >= flight + end)
                    {
                        Assert.That(w, Is.EqualTo(1f), "через кадр после старта полёта — клип, " + at + " кадр +" + k);
                        break;
                    }
                    prev = w;
                }
                Assert.That(inFlight, Is.True, at);
            }
            TestContext.WriteLine($"{fps} к/с: шаг за кадр до {worst:F3} (потолок {cap:F3}), доля CombatIdle до {worstShare * 100f:F1} %");
        }
        Assert.That(EntryStepCap(60), Is.EqualTo(.394f).Within(.002f), "раунд 2 при 60 к/с");
    }

    [Test]
    public void ExitStepsLiftOneFootAtATimeLeftThenRight()
    {
        Assert.That(PelagSquallClipRules.ExitStepsSpan, Is.EqualTo(2f));
        for (float p = 0f; p <= 2.0001f; p += .05f)
        {
            PelagSquallClipRules.ExitSteps(p, out float lt, out float ll, out float rt, out float rl);
            PelagSquallClipRules.ExitStep(p, out float soloTravel, out float soloLift);
            Assert.That(lt, Is.EqualTo(soloTravel).Within(1e-6f), "левая — как прежний шаг, p=" + p);
            Assert.That(ll, Is.EqualTo(soloLift).Within(1e-6f), "p=" + p);
            Assert.That(Math.Min(ll, rl), Is.LessThan(1e-4f), "обе стопы в воздухе не бывают, p=" + p);
            if (p <= 1f)
            {
                Assert.That(rt, Is.EqualTo(0f).Within(1e-6f), "пока шагает левая, правая стоит, p=" + p);
                Assert.That(rl, Is.EqualTo(0f).Within(1e-6f), "p=" + p);
            }
            else
            {
                Assert.That(lt, Is.EqualTo(1f).Within(1e-6f), "левая уже в позе смешивания, p=" + p);
                Assert.That(ll, Is.EqualTo(0f).Within(1e-6f), "p=" + p);
            }
        }
        PelagSquallClipRules.ExitSteps(1.5f, out _, out _, out float midTravel, out float midLift);
        Assert.That(midTravel, Is.EqualTo(.5f).Within(1e-5f));
        Assert.That(midLift, Is.EqualTo(1f).Within(1e-5f), "правая — та же дуга");
        PelagSquallClipRules.ExitSteps(2f, out float endLeft, out float endLeftLift, out float endRight, out float endRightLift);
        Assert.That(endLeft + endRight, Is.EqualTo(2f).Within(1e-5f), "обе в позе смешивания — снятие IK без скачка");
        Assert.That(endLeftLift + endRightLift, Is.EqualTo(0f).Within(1e-5f));
    }

    private static void AssertPose(PelagSquallPose pose, PelagSquallClip clip, float frame)
    {
        Assert.That(pose.Clip, Is.EqualTo(clip));
        Assert.That(pose.Frame, Is.EqualTo(frame).Within(1e-3f), clip.ToString());
    }
}
