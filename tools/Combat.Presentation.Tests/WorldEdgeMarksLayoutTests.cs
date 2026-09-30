using Game.Sim;
using Game.View;
using NUnit.Framework;

namespace Combat.Presentation.Tests
{
    /// <summary>
    /// Раскладка тлеющих меток у края экрана (2a, 30.09): за кадром ли цель, куда на край встаёт
    /// метка, как обходит панели HUD, как сливаются соседи и гаснет подпись; плюс пунктир дуги прыжка.
    /// </summary>
    public sealed class WorldEdgeMarksLayoutTests
    {
        // Экран 1920×1080, вставка — 60 px от края.
        static readonly EdgeRect Screen = new EdgeRect(0f, 0f, 1920f, 1080f);
        static readonly EdgeRect Inset = Screen.Inset(60f);
        static float P => WorldEdgeMarksLayout.Perimeter(Inset);

        // ---------------------------------------------------------------- за кадром

        [Test]
        public void TargetInsideViewHasNoMark()
        {
            Assert.IsFalse(WorldEdgeMarksLayout.OffScreen(Screen, 960f, 540f, true, false, 40f, 30f));
        }

        [Test]
        public void TargetOutsideOrBehindCameraIsOffScreen()
        {
            Assert.IsTrue(WorldEdgeMarksLayout.OffScreen(Screen, -200f, 540f, true, false, 40f, 30f));
            Assert.IsTrue(WorldEdgeMarksLayout.OffScreen(Screen, 960f, 540f, false, false, 40f, 30f), "за камерой — всегда за кадром");
            Assert.IsTrue(WorldEdgeMarksLayout.OffScreen(Screen, float.NaN, 540f, true, false, 40f, 30f));
        }

        [Test]
        public void HalfVisibleBodyStillCountsAsOffScreen()
        {
            // Центр тела в 20 px от края: видна половина — метка ещё нужна.
            Assert.IsTrue(WorldEdgeMarksLayout.OffScreen(Screen, 20f, 540f, true, false, 40f, 30f));
        }

        [Test]
        public void ShownMarkHidesOnlyWhenTargetIsWellInside()
        {
            // 50 px от края: новая метка не встаёт (глубже поля 40), показанная держится (нужно 40 + 30).
            Assert.IsFalse(WorldEdgeMarksLayout.OffScreen(Screen, 50f, 540f, true, false, 40f, 30f));
            Assert.IsTrue(WorldEdgeMarksLayout.OffScreen(Screen, 50f, 540f, true, true, 40f, 30f));
            Assert.IsFalse(WorldEdgeMarksLayout.OffScreen(Screen, 90f, 540f, true, true, 40f, 30f));
        }

        // ---------------------------------------------------------------- проекция на край

        [Test]
        public void RayFromHeroExitsOnTheMatchingEdge()
        {
            WorldEdgeMarksLayout.EdgePoint(Inset, 960f, 540f, -1f, 0f, out float x, out float y);
            Assert.AreEqual(60f, x, 1e-3f);
            Assert.AreEqual(540f, y, 1e-3f);
            WorldEdgeMarksLayout.EdgePoint(Inset, 960f, 540f, 0f, 5f, out x, out y);
            Assert.AreEqual(960f, x, 1e-3f);
            Assert.AreEqual(1020f, y, 1e-3f);
        }

        [Test]
        public void DiagonalRayStopsAtFirstEdge()
        {
            // Вправо-вверх под 45°: верх (480 px выше) ближе правого края (900 px правее).
            WorldEdgeMarksLayout.EdgePoint(Inset, 960f, 540f, 1f, 1f, out float x, out float y);
            Assert.AreEqual(1020f, y, 1e-3f);
            Assert.AreEqual(1440f, x, 1e-3f);
        }

        [Test]
        public void OriginOutsideInsetIsClampedAndZeroDirectionPointsDown()
        {
            WorldEdgeMarksLayout.EdgePoint(Inset, -500f, 540f, 1f, 0f, out float x, out float y);
            Assert.AreEqual(1860f, x, 1e-3f);
            WorldEdgeMarksLayout.EdgePoint(Inset, 960f, 540f, 0f, 0f, out x, out y);
            Assert.AreEqual(60f, y, 1e-3f);
            Assert.AreEqual(960f, x, 1e-3f);
        }

        // ---------------------------------------------------------------- обход края

        [Test]
        public void PerimeterRoundTripOnEverySide()
        {
            float[,] points = { { 500f, 60f }, { 1860f, 300f }, { 700f, 1020f }, { 60f, 800f } };
            int[] sides = { WorldEdgeMarksLayout.SideBottom, WorldEdgeMarksLayout.SideRight, WorldEdgeMarksLayout.SideTop, WorldEdgeMarksLayout.SideLeft };
            for (int i = 0; i < 4; i++)
            {
                float s = WorldEdgeMarksLayout.ToPerimeter(Inset, points[i, 0], points[i, 1]);
                WorldEdgeMarksLayout.FromPerimeter(Inset, s, out float x, out float y, out int side);
                Assert.AreEqual(points[i, 0], x, 1e-2f, "x, сторона " + i);
                Assert.AreEqual(points[i, 1], y, 1e-2f, "y, сторона " + i);
                Assert.AreEqual(sides[i], side);
            }
        }

        [Test]
        public void PerimeterGoesCounterClockwiseFromBottomLeft()
        {
            float w = Inset.Width, h = Inset.Height;
            Assert.AreEqual(0f, WorldEdgeMarksLayout.ToPerimeter(Inset, 60f, 60f), 1e-3f);
            Assert.AreEqual(w, WorldEdgeMarksLayout.ToPerimeter(Inset, 1860f, 60f), 1e-3f);
            Assert.AreEqual(w + h, WorldEdgeMarksLayout.ToPerimeter(Inset, 1860f, 1020f), 1e-3f);
            Assert.AreEqual(2f * w + h, WorldEdgeMarksLayout.ToPerimeter(Inset, 60f, 1020f), 1e-3f);
            Assert.AreEqual(2f * (w + h), P, 1e-3f);
        }

        [Test]
        public void WrapDeltaTakesTheShortWayRoundTheCorner()
        {
            Assert.AreEqual(20f, WorldEdgeMarksLayout.WrapDelta(P - 10f, 10f, P), 1e-3f);
            Assert.AreEqual(-20f, WorldEdgeMarksLayout.WrapDelta(10f, P - 10f, P), 1e-3f);
            Assert.AreEqual(0f, WorldEdgeMarksLayout.Wrap(P, P), 1e-3f);
            Assert.AreEqual(P - 5f, WorldEdgeMarksLayout.Wrap(-5f, P), 1e-3f);
        }

        [Test]
        public void GlideMovesTowardTargetAndSnapsAcrossTheScreen()
        {
            float moved = WorldEdgeMarksLayout.Glide(100f, 200f, P, 1f / 60f, 9f, P * .25f);
            Assert.Greater(moved, 100f);
            Assert.Less(moved, 200f);
            // Через угол: от конца обхода к началу — вперёд, а не назад через весь экран.
            float corner = WorldEdgeMarksLayout.Glide(P - 10f, 10f, P, 1f / 60f, 9f, P * .25f);
            Assert.IsTrue(corner > P - 10f || corner < 10f);
            // Цель ушла на другую сторону экрана — сразу туда.
            Assert.AreEqual(P * .6f, WorldEdgeMarksLayout.Glide(0f, P * .6f, P, 1f / 60f, 9f, P * .25f), 1e-2f);
            // Без времени — на месте.
            Assert.AreEqual(100f, WorldEdgeMarksLayout.Glide(100f, 200f, P, 0f, 9f, P * .25f), 1e-3f);
        }

        // ---------------------------------------------------------------- панели HUD

        [Test]
        public void PanelOnBottomEdgeBlocksItsSpanPlusRadius()
        {
            // Полоса способностей: x 700–1220, y 0–120 — перекрывает нижнюю сторону вставки (y = 60).
            var panels = new[] { new EdgeRect(700f, 0f, 1220f, 120f) };
            float[] from = new float[8], to = new float[8];
            int n = WorldEdgeMarksLayout.BlockedIntervals(Inset, panels, 1, 23f, from, to);
            Assert.AreEqual(1, n);
            Assert.AreEqual(700f - 23f - 60f, from[0], 1e-3f);
            Assert.AreEqual(1220f + 23f - 60f, to[0], 1e-3f);
        }

        [Test]
        public void PanelNearButNotOnEdgeBlocksNarrowerSpan()
        {
            // Низ панели в 13 px над линией края: круг радиуса 23 задевает её уже, чем на всю ширину + радиус.
            var panels = new[] { new EdgeRect(900f, 73f, 1000f, 200f) };
            float[] from = new float[8], to = new float[8];
            int n = WorldEdgeMarksLayout.BlockedIntervals(Inset, panels, 1, 23f, from, to);
            Assert.AreEqual(1, n);
            float reach = (float)System.Math.Sqrt(23f * 23f - 13f * 13f);
            Assert.AreEqual(900f - reach - 60f, from[0], 1e-2f);
            Assert.AreEqual(1000f + reach - 60f, to[0], 1e-2f);
            // Дальше радиуса — край свободен.
            panels[0] = new EdgeRect(900f, 90f, 1000f, 200f);
            Assert.AreEqual(0, WorldEdgeMarksLayout.BlockedIntervals(Inset, panels, 1, 23f, from, to));
        }

        [Test]
        public void CornerPanelBlocksBothSidesAsOneSpan()
        {
            // Миникарта в правом верхнем углу: закрыт кусок правой стороны и верха подряд.
            var panels = new[] { new EdgeRect(1640f, 780f, 1920f, 1080f) };
            float[] from = new float[8], to = new float[8];
            int n = WorldEdgeMarksLayout.BlockedIntervals(Inset, panels, 1, 23f, from, to);
            Assert.AreEqual(1, n, "угол — один сплошной кусок");
            float corner = WorldEdgeMarksLayout.ToPerimeter(Inset, 1860f, 1020f);
            Assert.Less(from[0], corner);
            Assert.Greater(to[0], corner);
        }

        [Test]
        public void BottomLeftCornerPanelWrapsThroughPerimeterStart()
        {
            // Портрет в левом нижнем углу: закрыт конец левой стороны и начало низа — один отрезок через ноль.
            var panels = new[] { new EdgeRect(0f, 0f, 420f, 260f) };
            float[] from = new float[8], to = new float[8];
            int n = WorldEdgeMarksLayout.BlockedIntervals(Inset, panels, 1, 23f, from, to);
            Assert.AreEqual(1, n);
            Assert.Greater(to[0], P, "отрезок идёт через начало обхода");
            // Место в самом углу выталкивается к ближнему свободному концу.
            float pushed = WorldEdgeMarksLayout.PushOut(10f, P, from, to, n);
            WorldEdgeMarksLayout.FromPerimeter(Inset, pushed, out float x, out float y, out _);
            Assert.IsTrue(x >= 420f + 23f - .01f || y >= 260f + 23f - .01f, "метка вне портрета: " + x + ", " + y);
        }

        [Test]
        public void PushOutGoesToNearestFreeEnd()
        {
            float[] from = { 100f }, to = { 300f };
            Assert.AreEqual(99.5f, WorldEdgeMarksLayout.PushOut(150f, P, from, to, 1), 1e-3f);
            Assert.AreEqual(300.5f, WorldEdgeMarksLayout.PushOut(260f, P, from, to, 1), 1e-3f);
            Assert.AreEqual(50f, WorldEdgeMarksLayout.PushOut(50f, P, from, to, 1), 1e-3f, "свободное место не трогается");
        }

        [Test]
        public void FullyBlockedEdgeLeavesMarkInPlace()
        {
            float[] from = { 0f }, to = { P };
            Assert.AreEqual(123f, WorldEdgeMarksLayout.PushOut(123f, P, from, to, 1), 1e-3f);
        }

        [Test]
        public void OverlappingPanelsMergeIntoOneSpan()
        {
            var panels = new[] { new EdgeRect(700f, 0f, 900f, 100f), new EdgeRect(880f, 0f, 1200f, 100f) };
            float[] from = new float[8], to = new float[8];
            Assert.AreEqual(1, WorldEdgeMarksLayout.BlockedIntervals(Inset, panels, 2, 10f, from, to));
            Assert.AreEqual(1200f + 10f - 60f, to[0], 1e-3f);
        }

        // ---------------------------------------------------------------- слияние и предел

        static EdgeMarkCandidate Mark(int key, int priority, float s, float distance = 10f, bool threat = false)
            => new EdgeMarkCandidate { Key = key, Priority = priority, S = s, Distance = distance, Count = 1, Threat = threat };

        [Test]
        public void NeighboursMergeIntoTheMostImportantMark()
        {
            var marks = new[]
            {
                Mark(3, WorldEdgeMarksLayout.RangedPriority, 510f),
                Mark(5, WorldEdgeMarksLayout.ElitePriority, 500f),
                Mark(7, WorldEdgeMarksLayout.RangedPriority, 900f),
            };
            var output = new EdgeMarkCandidate[8];
            int n = WorldEdgeMarksLayout.Merge(marks, marks.Length, P, 56f, 5, output);
            Assert.AreEqual(2, n);
            Assert.AreEqual(5, output[0].Key, "элита ведёт слитую метку");
            Assert.AreEqual(2, output[0].Count);
            Assert.AreEqual(500f, output[0].S, 1e-3f, "метка стоит на месте ведущей цели");
            Assert.AreEqual(7, output[1].Key);
            Assert.AreEqual(1, output[1].Count);
        }

        [Test]
        public void ThreatLeadsAndColoursMergedMark()
        {
            var marks = new[]
            {
                Mark(2, WorldEdgeMarksLayout.ElitePriority, 400f),
                Mark(9, WorldEdgeMarksLayout.ThreatPriority, 420f, threat: true),
            };
            var output = new EdgeMarkCandidate[8];
            int n = WorldEdgeMarksLayout.Merge(marks, 2, P, 56f, 5, output);
            Assert.AreEqual(1, n);
            Assert.AreEqual(9, output[0].Key);
            Assert.IsTrue(output[0].Threat);
            Assert.AreEqual(2, output[0].Count);
        }

        [Test]
        public void MergeWorksAcrossPerimeterStart()
        {
            var marks = new[] { Mark(1, WorldEdgeMarksLayout.ElitePriority, P - 5f), Mark(2, WorldEdgeMarksLayout.RangedPriority, 10f) };
            var output = new EdgeMarkCandidate[8];
            Assert.AreEqual(1, WorldEdgeMarksLayout.Merge(marks, 2, P, 56f, 5, output));
            Assert.AreEqual(2, output[0].Count);
        }

        [Test]
        public void AtMostMaxMarksAndNearestWinsWithinPriority()
        {
            var marks = new EdgeMarkCandidate[7];
            for (int i = 0; i < marks.Length; i++)
                marks[i] = Mark(i + 1, WorldEdgeMarksLayout.RangedPriority, 200f * i + 100f, distance: 30f - i);
            var output = new EdgeMarkCandidate[8];
            int n = WorldEdgeMarksLayout.Merge(marks, marks.Length, P, 56f, 5, output);
            Assert.AreEqual(5, n);
            // Ближние (большие номера — ближе) взяты, две дальние отброшены.
            for (int i = 0; i < n; i++) Assert.Greater(output[i].Key, 2);
        }

        [Test]
        public void MergedMarksAreFarEnoughApart()
        {
            var marks = new EdgeMarkCandidate[20];
            for (int i = 0; i < marks.Length; i++) marks[i] = Mark(i + 1, WorldEdgeMarksLayout.ElitePriority, 30f * i, distance: i);
            var output = new EdgeMarkCandidate[8];
            int n = WorldEdgeMarksLayout.Merge(marks, marks.Length, P, 56f, 8, output);
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    Assert.GreaterOrEqual(System.Math.Abs(WorldEdgeMarksLayout.WrapDelta(output[a].S, output[b].S, P)), 56f);
        }

        [Test]
        public void MergeOrderDoesNotDependOnInputOrder()
        {
            var a = new[] { Mark(4, 20, 100f, 5f), Mark(8, 20, 130f, 5f), Mark(6, 30, 800f, 9f) };
            var b = new[] { a[2], a[1], a[0] };
            var outA = new EdgeMarkCandidate[8];
            var outB = new EdgeMarkCandidate[8];
            int na = WorldEdgeMarksLayout.Merge(a, 3, P, 56f, 5, outA);
            int nb = WorldEdgeMarksLayout.Merge(b, 3, P, 56f, 5, outB);
            Assert.AreEqual(na, nb);
            for (int i = 0; i < na; i++)
            {
                Assert.AreEqual(outA[i].Key, outB[i].Key);
                Assert.AreEqual(outA[i].Count, outB[i].Count);
            }
        }

        // ---------------------------------------------------------------- что метить

        [Test]
        public void PriorityOrderThreatEliteRangedGoals()
        {
            Assert.AreEqual(WorldEdgeMarksLayout.ThreatPriority, WorldEdgeMarksLayout.EnemyPriority(true, false, false));
            Assert.AreEqual(WorldEdgeMarksLayout.ElitePriority, WorldEdgeMarksLayout.EnemyPriority(false, true, true));
            Assert.AreEqual(WorldEdgeMarksLayout.RangedPriority, WorldEdgeMarksLayout.EnemyPriority(false, false, true));
            Assert.AreEqual(0, WorldEdgeMarksLayout.EnemyPriority(false, false, false), "обычный ближний враг без атаки — без метки");
            Assert.Greater(WorldEdgeMarksLayout.RangedPriority, WorldEdgeMarksLayout.ExitPriority);
            Assert.Greater(WorldEdgeMarksLayout.ExitPriority, WorldEdgeMarksLayout.CachePriority);
        }

        [Test]
        public void ForestShootersAreRanged()
        {
            Assert.IsTrue(WorldEdgeMarksLayout.IsRanged(EnemyKind.ForestThorncaster));
            Assert.IsTrue(WorldEdgeMarksLayout.IsRanged(EnemyKind.ForestBud));
            Assert.IsFalse(WorldEdgeMarksLayout.IsRanged(EnemyKind.ForestWendigo));
            Assert.IsFalse(WorldEdgeMarksLayout.IsRanged(EnemyKind.ForestStonehoof));
        }

        [Test]
        public void EveryForestEnemyHasShortName()
        {
            foreach (EnemyKind kind in System.Enum.GetValues(typeof(EnemyKind)))
            {
                if (kind == EnemyKind.None) continue;
                string name = WorldEdgeMarksLayout.ShortName(kind);
                Assert.AreNotEqual("Враг", name, kind.ToString());
                Assert.LessOrEqual(name.Length, 12, "подпись метки короткая: " + name);
            }
            Assert.AreEqual("Шипомёт", WorldEdgeMarksLayout.ShortName(EnemyKind.ForestThorncaster));
        }

        // ---------------------------------------------------------------- подпись

        [Test]
        public void LabelFadesInHoldsThenLeavesIconOnly()
        {
            Assert.AreEqual(0f, WorldEdgeMarksLayout.LabelAlpha(0f));
            Assert.AreEqual(.5f, WorldEdgeMarksLayout.LabelAlpha(WorldEdgeMarksLayout.LabelIn * .5f), 1e-4f);
            Assert.AreEqual(1f, WorldEdgeMarksLayout.LabelAlpha(1.5f));
            float fading = WorldEdgeMarksLayout.LabelAlpha(WorldEdgeMarksLayout.LabelIn + WorldEdgeMarksLayout.LabelHold + WorldEdgeMarksLayout.LabelOut * .5f);
            Assert.Greater(fading, 0f);
            Assert.Less(fading, 1f);
            Assert.AreEqual(0f, WorldEdgeMarksLayout.LabelAlpha(WorldEdgeMarksLayout.LabelIn + WorldEdgeMarksLayout.LabelHold + WorldEdgeMarksLayout.LabelOut));
            Assert.AreEqual(0f, WorldEdgeMarksLayout.LabelAlpha(float.NaN));
        }

        [Test]
        public void LabelFadeNeverRises()
        {
            float previous = 1f;
            for (float age = WorldEdgeMarksLayout.LabelIn; age < 5f; age += .05f)
            {
                float alpha = WorldEdgeMarksLayout.LabelAlpha(age);
                Assert.LessOrEqual(alpha, previous + 1e-5f);
                previous = alpha;
            }
        }

        [Test]
        public void LabelTextAndMeters()
        {
            Assert.AreEqual("Вендиго · 14 м", WorldEdgeMarksLayout.Label("Вендиго", WorldEdgeMarksLayout.Meters(13.6f)));
            Assert.AreEqual(1, WorldEdgeMarksLayout.Meters(.2f), "ближе метра — «1 м», не «0 м»");
            Assert.AreEqual(15, WorldEdgeMarksLayout.Meters(14.5f));
            Assert.AreEqual("×3", WorldEdgeMarksLayout.CountText(3));
            Assert.AreEqual(string.Empty, WorldEdgeMarksLayout.CountText(1));
        }

        // ---------------------------------------------------------------- пунктир дуги

        [Test]
        public void ArcApexIsShareOfLengthCapped()
        {
            Assert.AreEqual(1.4f, HudRangePreviewArc.Apex(5f, .28f, 2.2f), 1e-4f);
            Assert.AreEqual(2.2f, HudRangePreviewArc.Apex(20f, .28f, 2.2f), 1e-4f);
            Assert.AreEqual(0f, HudRangePreviewArc.Apex(0f, .28f, 2.2f));
            Assert.AreEqual(0f, HudRangePreviewArc.Height(0f, 2f));
            Assert.AreEqual(0f, HudRangePreviewArc.Height(1f, 2f));
            Assert.AreEqual(2f, HudRangePreviewArc.Height(.5f, 2f), 1e-4f);
        }

        [Test]
        public void DashesSpanThePathExactly()
        {
            int count = HudRangePreviewArc.Dashes(6f, .3f, .2f);
            Assert.AreEqual(12, count);
            HudRangePreviewArc.Dash(0, count, .3f, .2f, .1f, .9f, out float first, out _);
            HudRangePreviewArc.Dash(count - 1, count, .3f, .2f, .1f, .9f, out _, out float last);
            Assert.AreEqual(.1f, first, 1e-5f, "первый штрих — от начала пути");
            Assert.AreEqual(.9f, last, 1e-5f, "последний кончается в конце, без обрубка");
            for (int i = 1; i < count; i++)
            {
                HudRangePreviewArc.Dash(i - 1, count, .3f, .2f, .1f, .9f, out _, out float end);
                HudRangePreviewArc.Dash(i, count, .3f, .2f, .1f, .9f, out float start, out _);
                Assert.Greater(start, end, "между штрихами — промежуток");
            }
            Assert.AreEqual(1, HudRangePreviewArc.Dashes(.1f, .3f, .2f), "короткий путь — один штрих");
            Assert.AreEqual(0, HudRangePreviewArc.Dashes(0f, .3f, .2f));
        }
    }
}
