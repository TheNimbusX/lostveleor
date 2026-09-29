using System;
using NUnit.Framework;
using Game.View;
using L = Game.View.SmokeRouteMapLayout;

// Карта тушью на закрытой дымной завесе (выбор владельца 30.09, концепты a5/a6): места узлов по глубине и
// развилке, кривая мазка, нажим и ход кисти, подпись «впереди» и расписание показа.
public sealed class SmokeRouteMapLayoutTests
{
    const float HalfScreen = 960f;

    [Test]
    public void NextArenaStandsAtNextXAndPastNodesStepLeft()
    {
        Assert.That(L.X(5, 5), Is.EqualTo(L.NextX));
        Assert.That(L.X(4, 5), Is.EqualTo(L.NextX - L.Spacing));
        for (int depth = 0; depth < 12; depth++)
            Assert.That(L.X(depth + 1, 12) - L.X(depth, 12), Is.EqualTo(L.Spacing).Within(1e-4f));
    }

    [Test]
    public void ArenaKeepsItsHeightFromMapToMap()
    {
        // Узел не прыгает по высоте от перехода к переходу: высота зависит только от глубины.
        for (int depth = 0; depth < 40; depth++)
        {
            float y = L.WaveY(depth);
            Assert.That(Math.Abs(y - L.RoadY), Is.LessThanOrEqualTo(L.Wave + 1e-4f));
        }
        Assert.That(L.WaveY(3), Is.Not.EqualTo(L.WaveY(4)), "дорога волнистая, а не прямая");
    }

    [Test]
    public void ForkIsAColumnFirstPathOnTop()
    {
        L.Branch(4, 0, 3, out float x0, out float y0);
        L.Branch(4, 1, 3, out float x1, out float y1);
        L.Branch(4, 2, 3, out float x2, out float y2);
        Assert.That(x0, Is.EqualTo(L.NextX));
        Assert.That(x1, Is.EqualTo(L.NextX));
        Assert.That(x2, Is.EqualTo(L.NextX));
        Assert.That(y0 - y1, Is.EqualTo(L.ForkSpread).Within(1e-4f));
        Assert.That(y1 - y2, Is.EqualTo(L.ForkSpread).Within(1e-4f));
        Assert.That(y1, Is.EqualTo(L.WaveY(4)).Within(1e-4f), "середина развилки — на волне дороги");
    }

    [Test]
    public void SingleBranchSitsOnTheRoad()
    {
        L.Branch(1, 0, 1, out float x, out float y);
        Assert.That(x, Is.EqualTo(L.NextX));
        Assert.That(y, Is.EqualTo(L.WaveY(1)).Within(1e-4f));
    }

    [Test]
    public void BranchClampsIndexAndCount()
    {
        L.Branch(3, 0, 3, out _, out float top);
        L.Branch(3, 2, 3, out _, out float bottom);
        L.Branch(3, -1, 3, out _, out float below0);
        L.Branch(3, 7, 3, out _, out float above);
        Assert.That(below0, Is.EqualTo(top));
        Assert.That(above, Is.EqualTo(bottom));
        L.Branch(3, 0, 0, out _, out float none);
        Assert.That(none, Is.EqualTo(L.WaveY(3)).Within(1e-4f));
    }

    [Test]
    public void ForkFitsBetweenTitleAndScreenBottom()
    {
        for (int next = 1; next < 40; next++)
        {
            L.Branch(next, 0, 3, out _, out float top);
            L.Branch(next, 2, 3, out _, out float bottom);
            // Верхний пустой круг — под нитью заголовка с запасом.
            Assert.That(top + L.RingHalf, Is.LessThan(L.ThreadY - 20f), "глубина " + next);
            // Подпись под нижним путём (если выбран он) — на экране.
            Assert.That(bottom - L.CaptionDrop - L.CaptionBelow, Is.GreaterThan(-540f + 30f), "глубина " + next);
        }
    }

    [Test]
    public void FirstShownDropsNodesBeyondTheLeftEdge()
    {
        // Дальше десяти арен: видны только те, чья середина не дальше EdgeCut за краем.
        int first = L.FirstShown(10, 11, HalfScreen);
        Assert.That(L.X(first, 11), Is.GreaterThanOrEqualTo(-HalfScreen - L.EdgeCut));
        Assert.That(L.X(first - 1, 11), Is.LessThan(-HalfScreen - L.EdgeCut));
        Assert.That(first, Is.EqualTo(8));
    }

    [Test]
    public void FirstShownKeepsAtMostMaxPast()
    {
        Assert.That(L.FirstShown(10, 11, 5000f), Is.EqualTo(10 - L.MaxPast + 1));
        Assert.That(L.FirstShown(2, 3, HalfScreen), Is.EqualTo(0), "лагерь ещё на экране");
        Assert.That(L.FirstShown(0, 1, HalfScreen), Is.EqualTo(0));
        Assert.That(L.FirstShown(-1, 0, HalfScreen), Is.EqualTo(0));
        // Даже на очень узком экране текущий узел остаётся.
        Assert.That(L.FirstShown(3, 4, 10f), Is.EqualTo(3));
    }

    [Test]
    public void EdgeAlphaFadesOnlyNearTheLeftEdge()
    {
        Assert.That(L.EdgeAlpha(-HalfScreen, HalfScreen), Is.EqualTo(0f));
        Assert.That(L.EdgeAlpha(-2000f, HalfScreen), Is.EqualTo(0f));
        Assert.That(L.EdgeAlpha(0f, HalfScreen), Is.EqualTo(1f));
        Assert.That(L.EdgeAlpha(L.X(4, 5), HalfScreen), Is.EqualTo(1f), "текущий узел не тает");
        float last = -1f;
        for (float x = -1000f; x <= -600f; x += 10f)
        {
            float a = L.EdgeAlpha(x, HalfScreen);
            Assert.That(a, Is.GreaterThanOrEqualTo(last));
            last = a;
        }
    }

    [Test]
    public void StrokeCurveHitsBothNodesAndLeavesThemLevel()
    {
        const int n = 33;
        var xs = new float[n];
        var ys = new float[n];
        var length = new float[n];
        float total = L.Sample(-130f, 60f, 200f, 260f, xs, ys, length);
        Assert.That(xs[0], Is.EqualTo(-130f).Within(1e-3f));
        Assert.That(ys[0], Is.EqualTo(60f).Within(1e-3f));
        Assert.That(xs[n - 1], Is.EqualTo(200f).Within(1e-3f));
        Assert.That(ys[n - 1], Is.EqualTo(260f).Within(1e-3f));
        Assert.That(length[0], Is.EqualTo(0f));
        for (int i = 1; i < n; i++) Assert.That(length[i], Is.GreaterThan(length[i - 1]));
        Assert.That(total, Is.EqualTo(length[n - 1]));
        float straight = (float)Math.Sqrt(330f * 330f + 200f * 200f);
        Assert.That(total, Is.GreaterThanOrEqualTo(straight - 1e-3f));
        Assert.That(total, Is.LessThan(straight * 1.3f), "S-кривая, а не петля");
        // Мазок выходит из узла и входит в следующий ровно, а не углом.
        Assert.That(Math.Abs(ys[1] - ys[0]), Is.LessThan(Math.Abs(xs[1] - xs[0]) * .2f));
        Assert.That(Math.Abs(ys[n - 1] - ys[n - 2]), Is.LessThan(Math.Abs(xs[n - 1] - xs[n - 2]) * .2f));
    }

    [Test]
    public void BrushStartsAtZeroEndsAtOneAndNeverGoesBack()
    {
        Assert.That(L.Brush(0f), Is.EqualTo(0f));
        Assert.That(L.Brush(1f), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(L.Brush(-1f), Is.EqualTo(0f));
        Assert.That(L.Brush(2f), Is.EqualTo(1f).Within(1e-6f));
        float last = 0f;
        for (int i = 1; i <= 100; i++)
        {
            float b = L.Brush(i / 100f);
            Assert.That(b, Is.GreaterThan(last), "кисть не замирает");
            last = b;
        }
    }

    [Test]
    public void PressureStaysInsideTheStroke()
    {
        foreach (float seed in new[] { 0f, .7f, 3.1f, 9.4f })
            for (int i = 0; i <= 50; i++)
            {
                float p = L.Pressure(i / 50f, seed);
                Assert.That(p, Is.InRange(.4f, 1.1f));
            }
        Assert.That(L.Pressure(.5f, 0f), Is.GreaterThan(L.Pressure(0f, 0f)), "кисть касается тонко, в середине — полный нажим");
    }

    [Test]
    public void AheadSaysTheMostImportantThing()
    {
        Assert.That(L.Ahead(new SmokeRouteAhead()), Is.Empty, "забег не найден — только номер арены");
        var boss = new SmokeRouteAhead { Known = true, Boss = true, BossName = "Хранитель леса", Elite = true, EliteName = "Вендиго", Hard = true };
        Assert.That(L.Ahead(boss), Is.EqualTo("Впереди — Хранитель леса"));
        Assert.That(L.Ahead(new SmokeRouteAhead { Known = true, Boss = true }), Is.EqualTo("Впереди — Хранитель"));
        var elite = new SmokeRouteAhead { Known = true, Elite = true, EliteName = "Вендиго", Hard = true, BonusGold = 70, Lesson = "Шипомёт" };
        Assert.That(L.Ahead(elite), Is.EqualTo("Впереди — элита: Вендиго"));
        Assert.That(L.Ahead(new SmokeRouteAhead { Known = true, Elite = true }), Is.EqualTo("Впереди — элита"));
        var hard = new SmokeRouteAhead { Known = true, Hard = true, BonusGold = 70, Lesson = "Шипомёт" };
        Assert.That(L.Ahead(hard), Is.EqualTo("Опасная арена · +70 золота за зачистку"));
        Assert.That(L.Ahead(new SmokeRouteAhead { Known = true, Hard = true }), Is.EqualTo("Опасная арена"));
        var lesson = new SmokeRouteAhead { Known = true, Lesson = "Шипомёт", Ambush = true };
        Assert.That(L.Ahead(lesson), Is.EqualTo("Впереди — новый враг: Шипомёт"));
        Assert.That(L.Ahead(new SmokeRouteAhead { Known = true, Ambush = true, Survival = true }), Is.EqualTo("Впереди — засада"));
        Assert.That(L.Ahead(new SmokeRouteAhead { Known = true, Survival = true }), Is.EqualTo("Впереди — выстоять до конца"));
        Assert.That(L.Ahead(new SmokeRouteAhead { Known = true, Shop = true }), Does.StartWith("Награда — магазин"));
        Assert.That(L.Ahead(new SmokeRouteAhead { Known = true }), Is.EqualTo("Награда — способность или талант"));
    }

    [Test]
    public void TitlesAreCachedAndStopsNamed()
    {
        Assert.That(L.Title(3), Is.EqualTo("Арена 3"));
        Assert.That(L.Title(3), Is.SameAs(L.Title(3)), "строка раз на глубину, без мусора на каждом переходе");
        Assert.That(L.Title(200), Is.EqualTo("Арена 200"));
        Assert.That(L.StopLabel(0, SmokeRouteSign.Camp), Is.EqualTo("Лагерь"));
        Assert.That(L.StopLabel(0, SmokeRouteSign.Repeat), Is.EqualTo("Заново"));
        Assert.That(L.StopLabel(2, SmokeRouteSign.Repeat), Is.EqualTo("Арена 2"));
    }

    [Test]
    public void SignPrefersBossThenDangerThenReward()
    {
        Assert.That(L.Sign(true, true, true), Is.EqualTo(SmokeRouteSign.Boss));
        Assert.That(L.Sign(false, true, true), Is.EqualTo(SmokeRouteSign.Hard));
        Assert.That(L.Sign(false, false, true), Is.EqualTo(SmokeRouteSign.Shop));
        Assert.That(L.Sign(false, false, false), Is.EqualTo(SmokeRouteSign.Upgrade));
    }

    [Test]
    public void TimelineRunsForkThenBrushThenLightThenCaption()
    {
        Assert.That(L.ForkAt, Is.LessThan(L.PaintAt));
        Assert.That(L.InkIn, Is.LessThanOrEqualTo(L.PaintAt + L.Arrival), "пройденная дорога проявлена до прихода кисти");
        Assert.That(L.Arrival - L.LightLead, Is.GreaterThan(0f));
        Assert.That(L.CaptionAt, Is.LessThan(L.SubAt));
        Assert.That(L.SubAt + L.CaptionTime, Is.LessThan(L.ReadUntil), "«впереди» дописано до конца показа");
        Assert.That(L.ReadUntil - (L.SubAt + L.CaptionTime), Is.GreaterThanOrEqualTo(.35f), "подпись успевают прочитать");
    }

    [Test]
    public void WholeTransitionStaysUnderTwoPointEightSeconds()
    {
        // Числа завесы (CampTransition.RollTime / OpenTime, SmokeTransition.MapStart): накат .6 с, карта с .55
        // наката, сим переходит на новую арену ≈ через .05 с после закрытия, рассеивание .85 с.
        const float roll = .6f, mapStart = .55f, arrive = .05f, open = .85f;
        float shownBeforeClosed = roll * (1f - mapStart);
        float paintFrom = Math.Max(L.PaintAt, shownBeforeClosed + arrive);
        float held = paintFrom + L.ReadUntil - shownBeforeClosed;
        float total = roll + held + open;
        Assert.That(L.DoneAt, Is.LessThanOrEqualTo(1.5f));
        Assert.That(total, Is.LessThanOrEqualTo(2.8f));
        // А карта видна достаточно, чтобы её прочитать.
        Assert.That(shownBeforeClosed + held, Is.InRange(1.0f, 1.9f));
    }
}
