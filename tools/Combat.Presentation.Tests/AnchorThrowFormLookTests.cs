using Game.Sim;
using Game.View;
using NUnit.Framework;

namespace Combat.Presentation.Tests
{
    /// <summary>
    /// Формы Броска якоря на стеке базы (PelagThrowFormLook, 08.10): палитры форм (зелень, индиго, маджента — без тёплых тонов),
    /// цвета частиц (база — ровно числа «холодного железа», формы — свой оттенок), тяга вида = кривая Sim, сеть Невода по снимку
    /// (ложится к рывку, гребёт хвостом к полукольцу ловли, стягивается к руке), оглушение Гарпуна = число Sim, борозда шире. Снимок — как в AnchorThrowVfxRulesTests (бросок 7 м).
    /// </summary>
    public sealed class AnchorThrowFormLookTests
    {
        private const int Cast = 100;

        private static AnchorThrowState Throw7(PelagForm form)
        {
            Fix64 reach = Fix64.FromInt(7);
            int flight = Simulation.AnchorThrowFlightTicks(reach);
            var s = new AnchorThrowState
            {
                Serial = 3, Form = form, HarpoonTarget = -1, Lanes = form == PelagForm.AnchorThrowFan ? 3 : 1,
                Center = new FixVec2(Fix64.Zero, Fix64.Zero), Dir = new FixVec2(Fix64.One, Fix64.Zero),
                Reach0 = reach, Step0 = (reach - Simulation.AbordageHandReach) / Fix64.FromInt(flight), Flight0 = flight,
                HalfWidth = Fix64.Ratio(45, 100), CastTick = Cast, WindupTicks = 2, ReleaseTick = Cast + 2,
                NetHalfWidth = form == PelagForm.AnchorThrowNet ? Simulation.AnchorThrowNetHalfWidth : Fix64.Zero,
                RingRadius = Fix64.Ratio(13, 10),
            };
            s.Origin = s.Center + s.Dir * Simulation.AbordageHandReach;
            s.FlightTicks = flight;
            s.TautTick = s.ReleaseTick + flight + Simulation.AnchorThrowTautTicks;
            s.ReturnTicks = Simulation.AnchorThrowReturnTicks(reach);
            s.CatchTick = s.TautTick + s.ReturnTicks;
            return s;
        }

        private static void Rgb(int hex, out int r, out int g, out int b) { r = (hex >> 16) & 255; g = (hex >> 8) & 255; b = hex & 255; }

        [Test]
        public void Palettes_AreTheFormColours_NoWarmTones()
        {
            Rgb(PelagThrowFormLook.Net.Mid, out int r, out int g, out int b);
            Assert.That(g, Is.GreaterThan(b).And.GreaterThan(r), "Невод — морская зелень");
            Rgb(PelagThrowFormLook.Fan.Mid, out r, out g, out b);
            Assert.That(b, Is.GreaterThan(r).And.GreaterThan(g), "Веер — индиго-фиолет");
            Rgb(PelagThrowFormLook.Harpoon.Mid, out r, out g, out b);
            Assert.That(r, Is.GreaterThan(g + 120));
            Assert.That(b, Is.GreaterThan(g + 80), "Гарпун — маджента, не красный");
            foreach (var p in new[] { PelagThrowFormLook.Net, PelagThrowFormLook.Fan, PelagThrowFormLook.Harpoon })
            {
                Rgb(p.Hot, out r, out g, out b);
                Assert.That(b, Is.GreaterThanOrEqualTo(System.Math.Min(r, g)), "сердцевина не уходит в жёлтое/кремовое");
            }
        }

        [Test]
        public void IsThrowForm_OnlyTheThreeForms()
        {
            Assert.That(PelagThrowFormLook.IsThrowForm(PelagForm.AnchorThrowNet), Is.True);
            Assert.That(PelagThrowFormLook.IsThrowForm(PelagForm.AnchorThrowFan), Is.True);
            Assert.That(PelagThrowFormLook.IsThrowForm(PelagForm.AnchorThrowHarpoon), Is.True);
            Assert.That(PelagThrowFormLook.IsThrowForm(PelagForm.None), Is.False);
        }

        [Test]
        public void Pull_AtWholeTicks_IsTheSimCurve()
        {
            AnchorThrowState s = Throw7(PelagForm.AnchorThrowNet);
            for (int k = -1; k <= s.ReturnTicks + 1; k++)
            {
                float expected = k <= 0 ? 0f : k >= s.ReturnTicks ? 1f : Simulation.MaelstromPullProgress(k, s.ReturnTicks).ToFloat();
                Assert.That(PelagThrowFormLook.Pull(s.TautTick + k, s.TautTick, s.ReturnTicks), Is.EqualTo(expected).Within(1e-5f));
            }
            float mid = PelagThrowFormLook.Pull(s.TautTick + 2.5f, s.TautTick, s.ReturnTicks);
            Assert.That(mid, Is.GreaterThan(PelagThrowFormLook.Pull(s.TautTick + 2, s.TautTick, s.ReturnTicks)));
            Assert.That(mid, Is.LessThan(PelagThrowFormLook.Pull(s.TautTick + 3, s.TautTick, s.ReturnTicks)));
        }

        [Test]
        public void Net_LiesAtTheTaut_CoversTheSimBand_RakesToTheRing()
        {
            AnchorThrowState s = Throw7(PelagForm.AnchorThrowNet);
            Assert.That(PelagThrowFormLook.NetOpen(s.HoldTick - .5f, s.HoldTick), Is.EqualTo(0f));
            Assert.That(PelagThrowFormLook.NetOpen(s.TautTick + 1, s.HoldTick), Is.EqualTo(1f).Within(1e-4f));
            PelagThrowFormLook.NetSpan(s, s.TautTick, float.MaxValue, out float near, out float far, out float restFar);
            // В рывок сеть — вся полоса Sim: от горла на цепи перед рукой (не на теле героя) до конца полосы (плюс край).
            Assert.That(near, Is.LessThanOrEqualTo(PelagThrowFormLook.NetNear + 1e-4f));
            Assert.That(near, Is.GreaterThan(Simulation.AbordageHandReach.ToFloat()));
            Assert.That(far, Is.EqualTo(restFar).Within(1e-4f));
            Assert.That(far, Is.GreaterThanOrEqualTo(s.Reach0.ToFloat()));
            // К ловле хвостовой край — у полукольца ловли (тела в сети), сеть не выворачивается.
            PelagThrowFormLook.NetSpan(s, s.CatchTick, float.MaxValue, out near, out far, out _);
            Assert.That(far, Is.EqualTo(s.RingRadius.ToFloat() + .35f).Within(1e-4f));
            Assert.That(far - near, Is.GreaterThanOrEqualTo(.5f - 1e-4f));
            float previous = float.MaxValue;
            for (float t = s.TautTick; t <= s.CatchTick; t += .25f)
            {
                PelagThrowFormLook.NetSpan(s, t, float.MaxValue, out _, out far, out _);
                Assert.That(far, Is.LessThanOrEqualTo(previous + 1e-5f), "хвост гребёт только к герою");
                previous = far;
            }
            // Стяжка после ловли — к руке.
            PelagThrowFormLook.NetSpan(s, s.CatchTick + PelagThrowFormLook.NetCollapseTicks, s.CatchTick, out near, out far, out _);
            Assert.That(far, Is.EqualTo(PelagThrowFormLook.NetNear).Within(1e-3f));
        }

        [Test]
        public void Net_NarrowAtTheThroat_FullBandFurther()
        {
            Assert.That(PelagThrowFormLook.NetHalfAt(0f, 1.5f), Is.EqualTo(PelagThrowFormLook.NetThroatHalf).Within(1e-4f));
            Assert.That(PelagThrowFormLook.NetHalfAt(PelagThrowFormLook.NetWiden, 1.5f), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(PelagThrowFormLook.NetGatherHalf(4f, 4f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(PelagThrowFormLook.NetGatherHalf(.4f, 6f), Is.LessThan(.7f));
            for (float b = -1f; b <= 1f; b += .05f)
                Assert.That(System.Math.Abs(PelagThrowFormLook.NetRagAt(b, 7)), Is.LessThanOrEqualTo(PelagThrowFormLook.NetRag + 1e-4f));
        }

        [Test]
        public void Tint_BaseIsTheIronNumbers_FormsKeepTheirHue()
        {
            PelagThrowFormLook.Tint b = PelagThrowFormLook.TintFor(PelagForm.None);
            Assert.That(b.Flare.R, Is.EqualTo(.20f).Within(1e-5f));
            Assert.That(b.Flare.G, Is.EqualTo(.45f).Within(1e-5f));
            Assert.That(b.Flare.B, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(b.SparkHot.B, Is.EqualTo(1.32f).Within(1e-5f));
            // Вспышка формы: ярчайший канал 1, оттенок — цвет формы (зелень, индиго, маджента), не белое пятно.
            PelagThrowFormLook.Tint net = PelagThrowFormLook.TintFor(PelagForm.AnchorThrowNet);
            Assert.That(net.Flare.Max, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(net.Flare.G, Is.GreaterThan(net.Flare.B).And.GreaterThan(net.Flare.R * 2f));
            PelagThrowFormLook.Tint fan = PelagThrowFormLook.TintFor(PelagForm.AnchorThrowFan);
            Assert.That(fan.Flare.B, Is.GreaterThan(fan.Flare.R).And.GreaterThan(fan.Flare.G * 1.5f));
            PelagThrowFormLook.Tint harpoon = PelagThrowFormLook.TintFor(PelagForm.AnchorThrowHarpoon);
            Assert.That(harpoon.Flare.R, Is.GreaterThan(harpoon.Flare.G * 3f));
            Assert.That(harpoon.Flare.B, Is.GreaterThan(harpoon.Flare.G * 2f));
            // Искры: горячая светлее цвета формы (белое ядро искры), сердцевина без жёлтого.
            foreach (var t in new[] { net, fan, harpoon })
            {
                Assert.That(t.SparkHot.R + t.SparkHot.G + t.SparkHot.B, Is.GreaterThan(t.Flare.R + t.Flare.G + t.Flare.B));
                Assert.That(t.SparkHot.B, Is.GreaterThanOrEqualTo(System.Math.Min(t.SparkHot.R, t.SparkHot.G) - 1e-4f));
            }
        }

        [Test]
        public void Linear_IsTheSrgbCurve()
        {
            PelagThrowFormLook.Rgb white = PelagThrowFormLook.Linear(0xFFFFFF), mid = PelagThrowFormLook.Linear(0x808080);
            Assert.That(white.R, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(mid.G, Is.EqualTo(.2158f).Within(1e-3f));
            Assert.That(PelagThrowFormLook.Linear(0x000000).B, Is.EqualTo(0f));
        }

        [Test]
        public void Harpoon_StunIsTheSimNumber()
        {
            AnchorThrowState s = Throw7(PelagForm.AnchorThrowHarpoon);
            Assert.That(PelagThrowFormLook.HarpoonStunTicks(s.ReturnTicks),
                Is.EqualTo(Simulation.AnchorThrowTautTicks + s.ReturnTicks + 30));
            // Долгая борозда Гарпуна шире базы (кадр form-harpoon-v2), у остальных — база как есть.
            Assert.That(PelagThrowFormLook.FurrowHalf(.5f, true), Is.GreaterThan(PelagThrowFormLook.FurrowHalf(.5f, false)));
            Assert.That(PelagThrowFormLook.FurrowHalf(.5f, false), Is.EqualTo(PelagAnchorThrowIronLook.FurrowHalf(.5f)).Within(1e-6f));
        }

        [Test]
        public void Ghost_AppearsAfterRelease_DissolvesAfterCatch()
        {
            AnchorThrowState s = Throw7(PelagForm.AnchorThrowFan);
            Assert.That(PelagThrowFormLook.GhostAppear(s.ReleaseTick, s.ReleaseTick), Is.EqualTo(0f));
            Assert.That(PelagThrowFormLook.GhostAppear(s.ReleaseTick + 1.2f, s.ReleaseTick), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(PelagThrowFormLook.GhostDissolve(s.CatchTick - 1, s.CatchTick), Is.EqualTo(0f));
            Assert.That(PelagThrowFormLook.GhostDissolve(s.CatchTick + 5, s.CatchTick), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(PelagThrowFormLook.GhostScale, Is.GreaterThan(1f));
            // У руки — размер настоящей головы, вдали — крупнее.
            Assert.That(PelagThrowFormLook.GhostGrow(0f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(PelagThrowFormLook.GhostGrow(6f), Is.EqualTo(PelagThrowFormLook.GhostScale).Within(1e-5f));
        }

        [Test]
        public void Forms_ThinnerStreaks_FanGhostLanesWithoutFlightStars()
        {
            // База — ровно V6; формы тоньше (кадры форм), Веер — меньше звёзд и на боковых полосах звезды полёта нет.
            Assert.That(PelagThrowFormLook.StreakScale(PelagForm.None), Is.EqualTo(1f));
            Assert.That(PelagThrowFormLook.FlareScale(PelagForm.None, 0), Is.EqualTo(1f));
            Assert.That(PelagThrowFormLook.FlightFlares(PelagForm.None, 0), Is.True);
            foreach (PelagForm f in new[] { PelagForm.AnchorThrowNet, PelagForm.AnchorThrowFan, PelagForm.AnchorThrowHarpoon })
            {
                Assert.That(PelagThrowFormLook.StreakScale(f), Is.InRange(.5f, .99f));
                Assert.That(PelagThrowFormLook.FlareScale(f, 0), Is.InRange(.4f, 1f));
                Assert.That(PelagThrowFormLook.FlightFlares(f, 0), Is.True);
            }
            Assert.That(PelagThrowFormLook.FlightFlares(PelagForm.AnchorThrowFan, 1), Is.False);
            Assert.That(PelagThrowFormLook.FlightFlares(PelagForm.AnchorThrowFan, 2), Is.False);
            Assert.That(PelagThrowFormLook.FlareScale(PelagForm.AnchorThrowFan, 1),
                Is.LessThan(PelagThrowFormLook.FlareScale(PelagForm.AnchorThrowFan, 0)));
        }
    }
}
