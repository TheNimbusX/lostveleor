using System;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Буря цветения — где урон, где укрытие (razlom/Assets/Game.View/ThicketStormDangerRules.cs,
/// владелец 02.10, п. 12): пока волна впереди, весь пол поляны — метка удара, круги света вырезаны.
/// Здесь — время поля по тикам Sim (заливка, появление, вспышка удара, угасание, снятие без
/// удара), места кругов волн и пол поляны (край, длина заливки).
/// </summary>
public sealed class ThicketStormDangerRulesTests
{
    private const int Start = 1000;
    private const int FirstImpact = Start + Simulation.ThicketStormFirstWaveTicks;
    private const int SecondImpact = FirstImpact + Simulation.ThicketStormSecondWaveTicks;

    private static ThicketStormDangerRules.Wave Pending(int start = Start, int impact = FirstImpact)
    {
        var wave = default(ThicketStormDangerRules.Wave);
        ThicketStormDangerRules.Observe(ref wave, start, impact, false);
        return wave;
    }

    // ------------------------------------------------------------ места кругов

    [Test]
    public void Slots_MatchTheSimStormLayout()
    {
        Assert.AreEqual(6, ThicketStormDangerRules.Slots, "шейдер держит _Safe0…_Safe5");
        Assert.AreEqual(Simulation.ThicketStormWaves * Simulation.ThicketStormSafeCircles, ThicketStormDangerRules.Slots);
        Assert.AreEqual(0, ThicketStormDangerRules.SlotOf(0, 0), "волна 1 — места 0–2 TryGetThicketShape");
        Assert.AreEqual(2, ThicketStormDangerRules.SlotOf(0, 2));
        Assert.AreEqual(3, ThicketStormDangerRules.SlotOf(1, 0), "волна 2 — места 3–5");
        Assert.AreEqual(5, ThicketStormDangerRules.SlotOf(1, 2));
        Assert.LessOrEqual(ThicketStormDangerRules.SlotOf(1, 2), Simulation.ThicketShapeSlots - 1);
    }

    // ------------------------------------------------------------ ждёт удара

    [Test]
    public void PendingWave_FillsFromZeroAtItsStartToOneAtTheImpact()
    {
        var wave = Pending();
        Assert.AreEqual(0f, ThicketStormDangerRules.LookOf(wave, Start).Progress, 1e-6f);
        Assert.AreEqual(.5f, ThicketStormDangerRules.LookOf(wave, Start + 30).Progress, 1e-6f);
        Assert.AreEqual(1f, ThicketStormDangerRules.LookOf(wave, FirstImpact).Progress, 1e-6f);
        Assert.AreEqual(1f, ThicketStormDangerRules.LookOf(wave, FirstImpact + 7).Progress, 1e-6f, "дальше не растёт");
        Assert.AreEqual(0f, ThicketStormDangerRules.LookOf(wave, Start - 3).Progress, 1e-6f, "до начала — пусто");
        float previous = -1f;
        for (float tick = Start; tick <= FirstImpact; tick += .25f)
        {
            float p = ThicketStormDangerRules.LookOf(wave, tick).Progress;
            Assert.GreaterOrEqual(p, previous, "заливка не откатывается, тик " + tick);
            previous = p;
        }
    }

    [Test]
    public void PendingWave_AppearsQuicklyWithoutFlash_AndItsSheltersShine()
    {
        var wave = Pending();
        var first = ThicketStormDangerRules.LookOf(wave, Start);
        Assert.AreEqual(0f, first.Opacity, 1e-6f, "весь пол разом не щёлкает");
        var mid = ThicketStormDangerRules.LookOf(wave, Start + ThicketStormDangerRules.FadeInTicks * .5f);
        Assert.That(mid.Opacity, Is.GreaterThan(0f).And.LessThan(1f));
        var shown = ThicketStormDangerRules.LookOf(wave, Start + ThicketStormDangerRules.FadeInTicks);
        Assert.AreEqual(1f, shown.Opacity, 1e-6f);
        Assert.LessOrEqual(ThicketStormDangerRules.FadeInTicks, 6f, "за 0,2 с — успеть прочитать 2-секундную волну");
        for (int tick = Start; tick < FirstImpact; tick++)
        {
            var look = ThicketStormDangerRules.LookOf(wave, tick);
            Assert.AreEqual(0f, look.Flash, "вспышка — только на ударе, тик " + tick);
            Assert.AreEqual(look.Opacity, look.Rim, 1e-6f, "золото укрытий — вместе с полем");
        }
    }

    [Test]
    public void SameTick_SameLook_PauseAndCaptureHoldTheFrame()
    {
        var wave = Pending();
        for (float tick = Start - 2; tick < FirstImpact + 12; tick += .37f)
        {
            var a = ThicketStormDangerRules.LookOf(wave, tick);
            var b = ThicketStormDangerRules.LookOf(wave, tick);
            Assert.AreEqual(a.Progress, b.Progress);
            Assert.AreEqual(a.Opacity, b.Opacity);
            Assert.AreEqual(a.Flash, b.Flash);
            Assert.AreEqual(a.Rim, b.Rim);
        }
    }

    [Test]
    public void Hourglass_MovesTheImpact_FillStretchesAndStillEndsOnTheNewImpact()
    {
        var wave = Pending();
        float before = ThicketStormDangerRules.LookOf(wave, Start + 30).Progress;
        ThicketStormDangerRules.Observe(ref wave, Start, FirstImpact + Simulation.ThicketHourglassShiftTicks, false);
        float after = ThicketStormDangerRules.LookOf(wave, Start + 30).Progress;
        Assert.Less(after, before, "Часы: удар позже — заливка тянется");
        Assert.AreEqual(1f, ThicketStormDangerRules.LookOf(wave, FirstImpact + Simulation.ThicketHourglassShiftTicks).Progress, 1e-6f);
        Assert.Less(ThicketStormDangerRules.LookOf(wave, FirstImpact).Progress, 1f);
    }

    // ------------------------------------------------------------ удар

    [Test]
    public void ResolvedWave_FlashesOnImpactThenFades_SheltersGoFirst()
    {
        var wave = Pending();
        ThicketStormDangerRules.Observe(ref wave, Start, FirstImpact, true);
        var hit = ThicketStormDangerRules.LookOf(wave, FirstImpact);
        Assert.AreEqual(1f, hit.Progress, 1e-6f);
        Assert.AreEqual(1f, hit.Opacity, 1e-6f);
        Assert.AreEqual(1f, hit.Flash, 1e-6f, "вспышка на тике удара");
        Assert.AreEqual(1f, hit.Rim, 1e-6f);

        var afterFlash = ThicketStormDangerRules.LookOf(wave, FirstImpact + ThicketStormDangerRules.FlashTicks);
        Assert.AreEqual(0f, afterFlash.Flash, 1e-6f);
        Assert.Greater(afterFlash.Opacity, 0f, "поле гаснет дольше вспышки");

        var rimGone = ThicketStormDangerRules.LookOf(wave, FirstImpact + ThicketStormDangerRules.RimFadeTicks);
        Assert.AreEqual(0f, rimGone.Rim, 1e-6f, "после удара круг уже не укрытие");

        var gone = ThicketStormDangerRules.LookOf(wave, FirstImpact + ThicketStormDangerRules.ResolvedFadeTicks);
        Assert.IsFalse(gone.Visible, "между волнами старая гаснет");
        Assert.Less(ThicketStormDangerRules.ResolvedFadeTicks, Simulation.ThicketStormSecondWaveTicks,
            "первая волна гаснет задолго до удара второй");
        Assert.LessOrEqual(ThicketStormDangerRules.ResolvedFadeTicks, Simulation.ThicketStormRecoveryTicks,
            "вторая — пока босс ещё стоит после бури");

        float previous = 2f;
        for (float tick = FirstImpact; tick <= FirstImpact + ThicketStormDangerRules.ResolvedFadeTicks; tick += .5f)
        {
            float o = ThicketStormDangerRules.LookOf(wave, tick).Opacity;
            Assert.LessOrEqual(o, previous, "угасание без всплесков, тик " + tick);
            previous = o;
        }
    }

    [Test]
    public void SecondWave_StartsAtTheFirstImpact_AndFillsToItsOwn()
    {
        var second = Pending(FirstImpact, SecondImpact);
        Assert.AreEqual(0f, ThicketStormDangerRules.LookOf(second, FirstImpact).Progress, 1e-6f);
        Assert.AreEqual(1f, ThicketStormDangerRules.LookOf(second, SecondImpact).Progress, 1e-6f);
        Assert.IsFalse(ThicketStormDangerRules.LookOf(second, FirstImpact - 1).Visible, "до удара первой второй нет");
    }

    // ------------------------------------------------------------ снятие и конец бури

    [Test]
    public void LostPendingWave_IsCancelled_FreezesAndFadesWithoutFlash()
    {
        var wave = Pending();
        int cancel = Start + 20;
        ThicketStormDangerRules.Lose(ref wave, cancel);
        Assert.IsTrue(wave.Cancelled);
        float frozen = ThicketStormDangerRules.Progress(cancel, Start, FirstImpact);
        for (int tick = cancel; tick <= cancel + Simulation.TelegraphLingerTicks; tick++)
        {
            var look = ThicketStormDangerRules.LookOf(wave, tick);
            Assert.AreEqual(frozen, look.Progress, 1e-6f, "заливка замерла, тик " + tick);
            Assert.AreEqual(0f, look.Flash, "удара не было");
        }
        Assert.AreEqual(1f, ThicketStormDangerRules.LookOf(wave, cancel).Opacity, 1e-6f);
        Assert.IsFalse(ThicketStormDangerRules.LookOf(wave, cancel + Simulation.TelegraphLingerTicks).Visible);

        // Повторная потеря не сдвигает тик снятия.
        ThicketStormDangerRules.Lose(ref wave, cancel + 3);
        Assert.AreEqual(cancel, wave.CancelTick);
    }

    [Test]
    public void LostResolvedWave_KeepsItsImpactFade()
    {
        var wave = Pending(FirstImpact, SecondImpact);
        ThicketStormDangerRules.Observe(ref wave, FirstImpact, SecondImpact, true);
        // Буря кончилась (стойка после второй волны снята) — волна доживает свою вспышку.
        ThicketStormDangerRules.Lose(ref wave, SecondImpact + 2);
        Assert.IsFalse(wave.Cancelled);
        var look = ThicketStormDangerRules.LookOf(wave, SecondImpact + 2);
        Assert.Greater(look.Opacity, 0f);
        Assert.Greater(look.Flash, 0f);
    }

    [Test]
    public void CancelledDuringFadeIn_DoesNotPop()
    {
        var wave = Pending();
        ThicketStormDangerRules.Lose(ref wave, Start + 1);
        float o = ThicketStormDangerRules.LookOf(wave, Start + 1).Opacity;
        Assert.Less(o, .5f, "снятая, едва появившись, не вспыхивает целиком");
    }

    [Test]
    public void AbsentWave_IsInvisible()
    {
        var none = default(ThicketStormDangerRules.Wave);
        Assert.IsFalse(ThicketStormDangerRules.LookOf(none, Start).Visible);
        ThicketStormDangerRules.Lose(ref none, Start);
        Assert.IsFalse(none.Cancelled, "не было — нечего снимать");
        Assert.IsFalse(ThicketStormDangerRules.LookOf(Pending(), float.NaN).Visible);
    }

    // ------------------------------------------------------------ пол поляны

    private static GladeRegion BossFloor()
        => new GladeRegion(new FixVec2(Fix64.FromInt(40), Fix64.FromInt(30)), GladeLayout.BossClearingRadii, GladeShape.Rounded);

    [Test]
    public void FloorMask_CoversTheWholeBossFloor_AndDissolvesPastItsEdge()
    {
        var glade = BossFloor();
        double cx = 40, cz = 30;
        double hw = GladeLayout.BossFloorHalfWidth.ToDouble(), hd = GladeLayout.BossFloorHalfDepth.ToDouble();
        Assert.AreEqual(1f, ThicketStormDangerRules.FloorMask(glade, cx, cz), 1e-6f, "середина");
        Assert.AreEqual(1f, ThicketStormDangerRules.FloorMask(glade, cx + hw - .6, cz), 1e-6f, "у кромки по X — ещё поле");
        Assert.AreEqual(1f, ThicketStormDangerRules.FloorMask(glade, cx, cz - hd + .45), 1e-6f, "у кромки по Z (вход) — ещё поле");
        Assert.AreEqual(1f, ThicketStormDangerRules.FloorMask(glade, cx + .7 * hw, cz + .7 * hd), 1e-6f, "скруглённый угол");
        float edge = ThicketStormDangerRules.FloorMask(glade, cx + hw, cz);
        Assert.That(edge, Is.GreaterThan(0f).And.LessThan(1f), "на самой кромке — растворяется");
        Assert.AreEqual(0f, ThicketStormDangerRules.FloorMask(glade, cx + hw + .8, cz), 1e-6f, "за каймой — нет");
        Assert.AreEqual(0f, ThicketStormDangerRules.FloorMask(glade, cx, cz + hd + .8), 1e-6f);
        Assert.AreEqual(0f, ThicketStormDangerRules.FloorMask(glade, cx + hw, cz + hd), 1e-6f, "угол рамки — лес");
    }

    [Test]
    public void FillReach_FromTheBoss_ReachesTheFarthestFloorPoint()
    {
        var glade = BossFloor();
        var radii = glade.Radii;
        float rx = radii.X.ToFloat(), rz = radii.Y.ToFloat();
        const int n = 48;
        int count = (n + 1) * (n + 1);
        var xs = new float[count]; var zs = new float[count]; var mask = new float[count];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                int i = z * (n + 1) + x;
                xs[i] = 40f - rx + 2f * rx * x / n;
                zs[i] = 30f - rz + 2f * rz * z / n;
                mask[i] = ThicketStormDangerRules.FloorMask(glade, xs[i], zs[i]);
            }
        float centre = ThicketStormDangerRules.FillReach(40f, 30f, xs, zs, mask, count, 12f);
        // Скруглённый пол 20 × 15: дальше всего — скругление угла, ~10,5–11 м от середины.
        Assert.That(centre, Is.GreaterThan(10f).And.LessThan(12.5f));
        float corner = ThicketStormDangerRules.FillReach(34f, 26f, xs, zs, mask, count, 12f);
        Assert.Greater(corner, centre, "босс у края — фронту дальше идти до противоположного");
        Assert.AreEqual(12f, ThicketStormDangerRules.FillReach(0f, 0f, null, null, null, 0, 12f), 1e-6f, "пола нет — запасная длина");
        Assert.AreEqual(1f, ThicketStormDangerRules.FillReach(0f, 0f, new float[0], new float[0], new float[0], 0, 0f), 1e-6f);
    }
}
