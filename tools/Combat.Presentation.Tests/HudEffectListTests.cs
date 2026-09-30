using Game.Sim;
using Game.View;
using NUnit.Framework;

namespace Combat.Presentation.Tests
{
    /// <summary>
    /// Строка эффектов над портретом (этап 4, кадр 1a): порядок, секунды в углу значка, стаки «×2»,
    /// переполнение «+N», полная длина кольца и движение строки (HudEffectList, HudEffectRowMath).
    /// </summary>
    public sealed class HudEffectListTests
    {
        const int Tps = Simulation.TicksPerSecond;

        static HudEffectList Commit(HudEffectList list, params (HudEffectKind Kind, int Ticks)[] effects)
        {
            list.Begin();
            foreach (var (kind, ticks) in effects) list.Report(kind, ticks);
            list.Commit();
            return list;
        }

        [Test]
        public void OrderIsFixedByKindNotByArrival()
        {
            var list = new HudEffectList(Tps, 0);
            Commit(list, (HudEffectKind.Blaze, 60), (HudEffectKind.Resin, 90), (HudEffectKind.Root, 30), (HudEffectKind.Slow, 45));
            Assert.AreEqual(4, list.Count);
            Assert.AreEqual(HudEffectKind.Root, list[0]);
            Assert.AreEqual(HudEffectKind.Slow, list[1]);
            Assert.AreEqual(HudEffectKind.Resin, list[2]);
            Assert.AreEqual(HudEffectKind.Blaze, list[3]);
        }

        [Test]
        public void ControlComesFirstThenPotionsArtifactBlaze()
        {
            // Порядок владельца: контроль, потом зелья, артефакт, Blaze.
            Assert.Less((int)HudEffectKind.Root, (int)HudEffectKind.Resin);
            Assert.Less((int)HudEffectKind.Stun, (int)HudEffectKind.Resin);
            Assert.Less((int)HudEffectKind.ControlImmune, (int)HudEffectKind.Resin);
            Assert.Less((int)HudEffectKind.Clear, (int)HudEffectKind.Artifact);
            Assert.Less((int)HudEffectKind.Artifact, (int)HudEffectKind.Blaze);
            Assert.AreEqual(HudEffectList.KindCount - 1, (int)HudEffectKind.Blaze);
        }

        [TestCase(0, 0)]
        [TestCase(-5, 0)]
        [TestCase(1, 1)]
        [TestCase(30, 1)]
        [TestCase(31, 2)]
        [TestCase(89, 3)]
        [TestCase(90, 3)]
        [TestCase(180, 6)]
        public void SecondsRoundUpAndNeverShowZeroForLiveEffect(int ticks, int seconds)
        {
            Assert.AreEqual(seconds, HudEffectList.SecondsOf(ticks, Tps));
        }

        [Test]
        public void SecondsLabelsAreCachedAndCapped()
        {
            Assert.AreEqual(string.Empty, HudEffectList.SecondsLabel(0));
            Assert.AreEqual("3", HudEffectList.SecondsLabel(3));
            Assert.AreEqual("12", HudEffectList.SecondsLabel(12));
            Assert.AreEqual("99+", HudEffectList.SecondsLabel(140));
            // Одна и та же строка — без выделения памяти в кадре.
            Assert.AreSame(HudEffectList.SecondsLabel(7), HudEffectList.SecondsLabel(7));
        }

        [Test]
        public void StacksShowOnlyFromTwo()
        {
            Assert.AreEqual(string.Empty, HudEffectList.StacksLabel(0));
            Assert.AreEqual(string.Empty, HudEffectList.StacksLabel(1));
            Assert.AreEqual("×2", HudEffectList.StacksLabel(2));
            Assert.AreEqual("×5", HudEffectList.StacksLabel(5));
            Assert.AreSame(HudEffectList.StacksLabel(3), HudEffectList.StacksLabel(3));
        }

        [Test]
        public void StacksAreKeptAndNewStackRefreshes()
        {
            var list = new HudEffectList(Tps, 0);
            list.Begin();
            list.Report(HudEffectKind.Blaze, 90, 90, 1);
            list.Commit();
            Assert.AreEqual(1, list.Stacks(HudEffectKind.Blaze));
            list.Begin();
            list.Report(HudEffectKind.Blaze, 80, 90, 2);
            list.Commit();
            Assert.AreEqual(2, list.Stacks(HudEffectKind.Blaze));
            Assert.IsTrue(list.Refreshed(HudEffectKind.Blaze), "ещё один стак — круг вздрагивает");
            list.Begin();
            list.Commit();
            Assert.AreEqual(1, list.Stacks(HudEffectKind.Blaze), "кончился — стаки сброшены");
        }

        [Test]
        public void OverflowKeepsThreatsAndFoldsTheTailIntoPlusN()
        {
            var list = new HudEffectList(Tps, 4);
            Commit(list, (HudEffectKind.Root, 30), (HudEffectKind.Slow, 30), (HudEffectKind.Resin, 30),
                (HudEffectKind.Surge, 30), (HudEffectKind.Artifact, 30), (HudEffectKind.Blaze, 30));
            // Мест 4 вместе с «+N»: три круга и «+3».
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(3, list.Overflow);
            Assert.IsTrue(list.Shown(HudEffectKind.Root));
            Assert.IsTrue(list.Shown(HudEffectKind.Slow));
            Assert.IsTrue(list.Shown(HudEffectKind.Resin));
            Assert.IsFalse(list.Shown(HudEffectKind.Blaze));
            Assert.IsTrue(list.Active(HudEffectKind.Blaze));
            Assert.AreEqual("+3", HudEffectList.OverflowLabel(list.Overflow));
        }

        [Test]
        public void ExactlyMaxVisibleNeedsNoPlusN()
        {
            var list = new HudEffectList(Tps, 3);
            Commit(list, (HudEffectKind.Root, 30), (HudEffectKind.Resin, 30), (HudEffectKind.Blaze, 30));
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(0, list.Overflow);
            Assert.AreEqual(string.Empty, HudEffectList.OverflowLabel(0));
        }

        [Test]
        public void AppearRefreshEndAreReportedOnce()
        {
            var list = new HudEffectList(Tps, 0);
            Commit(list, (HudEffectKind.Resin, Simulation.PotionEffectTicks));
            Assert.IsTrue(list.Appeared(HudEffectKind.Resin));
            Commit(list, (HudEffectKind.Resin, Simulation.PotionEffectTicks - 10));
            Assert.IsFalse(list.Appeared(HudEffectKind.Resin));
            Assert.IsFalse(list.Refreshed(HudEffectKind.Resin));
            // Выпита заново: остаток вырос.
            Commit(list, (HudEffectKind.Resin, Simulation.PotionEffectTicks));
            Assert.IsTrue(list.Refreshed(HudEffectKind.Resin));
            Commit(list);
            Assert.IsTrue(list.Ended(HudEffectKind.Resin));
            Assert.IsFalse(list.Active(HudEffectKind.Resin));
            Commit(list);
            Assert.IsFalse(list.Ended(HudEffectKind.Resin), "вспышка ухода — один раз");
        }

        [Test]
        public void RingUsesKnownFullOrPeakSinceAppear()
        {
            var list = new HudEffectList(Tps, 0);
            // Зелье: полная длина известна — кольцо уже неполное, если первый кадр пришёл позже.
            list.Begin();
            list.Report(HudEffectKind.Surge, 120, Simulation.PotionEffectTicks);
            list.Commit();
            Assert.AreEqual(120f / Simulation.PotionEffectTicks, list.Fill(HudEffectKind.Surge), 1e-5f);
            // Корни: длину сим не отдаёт — полное кольцо в миг появления, дальше убывает.
            Commit(list, (HudEffectKind.Root, 60));
            Assert.AreEqual(1f, list.Fill(HudEffectKind.Root), 1e-5f);
            Commit(list, (HudEffectKind.Root, 30));
            Assert.AreEqual(.5f, list.Fill(HudEffectKind.Root), 1e-5f);
            // Замедление продлили — кольцо снова полное от нового остатка.
            Commit(list, (HudEffectKind.Slow, 40));
            Commit(list, (HudEffectKind.Slow, 20));
            Commit(list, (HudEffectKind.Slow, 80));
            Assert.IsTrue(list.Refreshed(HudEffectKind.Slow));
            Assert.AreEqual(1f, list.Fill(HudEffectKind.Slow), 1e-5f);
        }

        [Test]
        public void ClearDropsEverythingWithoutEndFlash()
        {
            var list = new HudEffectList(Tps, 0);
            Commit(list, (HudEffectKind.Stun, 30), (HudEffectKind.Blaze, 30));
            list.Clear();
            Assert.AreEqual(0, list.Count);
            Commit(list);
            Assert.IsFalse(list.Ended(HudEffectKind.Stun));
            Assert.IsFalse(list.Ended(HudEffectKind.Blaze));
        }

        [Test]
        public void TonesReadThreatGuardBoon()
        {
            Assert.AreEqual(HudEffectTone.Threat, HudEffectList.ToneOf(HudEffectKind.Root));
            Assert.AreEqual(HudEffectTone.Threat, HudEffectList.ToneOf(HudEffectKind.Stun));
            Assert.AreEqual(HudEffectTone.Threat, HudEffectList.ToneOf(HudEffectKind.Slow));
            Assert.AreEqual(HudEffectTone.Guard, HudEffectList.ToneOf(HudEffectKind.ControlImmune));
            Assert.AreEqual(HudEffectTone.Guard, HudEffectList.ToneOf(HudEffectKind.Clear));
            Assert.AreEqual(HudEffectTone.Boon, HudEffectList.ToneOf(HudEffectKind.Resin));
            Assert.AreEqual(HudEffectTone.Boon, HudEffectList.ToneOf(HudEffectKind.Artifact));
            Assert.AreEqual(HudEffectTone.Boon, HudEffectList.ToneOf(HudEffectKind.Blaze));
        }

        [Test]
        public void EveryKindHasNameAndLine()
        {
            for (int i = 0; i < HudEffectList.KindCount; i++)
            {
                var kind = (HudEffectKind)i;
                Assert.IsNotEmpty(HudEffectTexts.Name(kind), kind.ToString());
                if (kind != HudEffectKind.Artifact) Assert.IsNotEmpty(HudEffectTexts.Line(kind), kind.ToString());
            }
            Assert.AreEqual("Бег медленнее на 30%", HudEffectTexts.Line(HudEffectKind.Slow, 30));
            Assert.AreEqual("Ещё 3 с", HudEffectTexts.Remaining(3));
            foreach (RunArtifact artifact in new[] { RunArtifact.SunSeal, RunArtifact.VengeanceMirror, RunArtifact.WinterHeart,
                         RunArtifact.Hourglass, RunArtifact.VoidVisage, RunArtifact.CrimsonHeart, RunArtifact.GuardianVow })
                Assert.IsNotEmpty(HudEffectTexts.ArtifactLine(artifact), artifact.ToString());
        }

        [Test]
        public void RowPlacesCirclesByCumulativeWeight()
        {
            float[] weights = { 1f, 0f, .5f, 1f };
            float[] xs = new float[4];
            float width = HudEffectRowMath.Place(weights, 4, 52f, xs);
            Assert.AreEqual(0f, xs[0], 1e-4f);
            Assert.AreEqual(52f, xs[1], 1e-4f);
            Assert.AreEqual(52f, xs[2], 1e-4f, "нулевой вес места не занимает");
            Assert.AreEqual(52f + 26f, xs[3], 1e-4f, "половина веса — половина шага (сглаживание в середине не сдвигает)");
            Assert.AreEqual(52f * 2.5f, width, 1e-4f);
        }

        [Test]
        public void WeightsStepTowardTargetAndClamp()
        {
            Assert.AreEqual(.5f, HudEffectRowMath.StepWeight(0f, true, .1f, .2f, .2f), 1e-5f);
            Assert.AreEqual(1f, HudEffectRowMath.StepWeight(.9f, true, .1f, .2f, .2f), 1e-5f);
            Assert.AreEqual(0f, HudEffectRowMath.StepWeight(.1f, false, .1f, .2f, .2f), 1e-5f);
            Assert.AreEqual(.3f, HudEffectRowMath.StepWeight(.3f, true, 0f, .2f, .2f), 1e-5f, "без времени — на месте");
            Assert.AreEqual(1f, HudEffectRowMath.StepWeight(0f, true, .016f, 0f, .2f), 1e-5f, "нулевое время — сразу");
        }

        [Test]
        public void PopOvershootsThenSettles()
        {
            Assert.AreEqual(.55f, HudEffectRowMath.PopScale(0f), 1e-4f);
            Assert.Greater(HudEffectRowMath.PopScale(.45f), 1.05f);
            Assert.AreEqual(1f, HudEffectRowMath.PopScale(1f), 1e-4f);
            Assert.AreEqual(1f, HudEffectRowMath.PopScale(3f), 1e-4f);
        }

        [Test]
        public void LeaveFlashesOnceThenFades()
        {
            Assert.AreEqual(1f, HudEffectRowMath.LeaveFlash(0f), 1e-4f);
            Assert.AreEqual(0f, HudEffectRowMath.LeaveFlash(.3f), 1e-4f);
            Assert.AreEqual(0f, HudEffectRowMath.LeaveFlash(.9f), 1e-4f);
            Assert.AreEqual(1f, HudEffectRowMath.LeaveAlpha(.2f), 1e-4f, "во время вспышки круг виден целиком");
            Assert.AreEqual(0f, HudEffectRowMath.LeaveAlpha(1f), 1e-4f);
            Assert.Less(HudEffectRowMath.LeaveScale(1f), .75f);
        }
    }
}
