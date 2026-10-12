using Game.Sim;
using Game.View;
using NUnit.Framework;

namespace Combat.Presentation.Tests
{
    /// <summary>
    /// База Броска якоря «холодное железо» (PelagAnchorThrowIronLook, 08.10; кадры base-hit-v1 / base-yank-v1): палитра —
    /// Крушения на стеке сабли, край светящихся вещей кобальтом (не чернила), струна — комета в полёте и ровная в натяг,
    /// импульс рывка бежит от головы к руке, шевроны — только между рывком и ловлей, борозда — шириной в тело.
    /// </summary>
    public sealed class AnchorThrowIronLookTests
    {
        private static void Rgb(int hex, out int r, out int g, out int b) { r = (hex >> 16) & 255; g = (hex >> 8) & 255; b = hex & 255; }

        [Test]
        public void Palette_IsTheWreckBase_EdgeIsCobaltNotInk()
        {
            Assert.That(PelagAnchorThrowIronLook.DeepHex, Is.EqualTo(0x0A1F4A));
            Assert.That(PelagAnchorThrowIronLook.MidHex, Is.EqualTo(PelagWreckComboLook.MidHex(PelagForm.None)));
            Assert.That(PelagAnchorThrowIronLook.LightHex, Is.EqualTo(0x8FC4FF));
            Rgb(PelagWreckComboLook.InkHex, out int ir, out int ig, out int ib);
            Rgb(PelagAnchorThrowIronLook.EdgeHex, out int r, out int g, out int b);
            Assert.That(b, Is.GreaterThan(ib + 80), "край светящегося — насыщенный кобальт, светлее чернил Крушения");
            Assert.That(b, Is.GreaterThan(r + 60).And.GreaterThan(g + 40), "без тёплых тонов");
            Rgb(PelagAnchorThrowIronLook.ChipEdgeHex, out r, out g, out b);
            Assert.That(b, Is.GreaterThan(ib + 25), "край скола — тёмный кобальт, не чернила");
            Assert.That(b, Is.LessThan(PelagAnchorThrowIronLook.EdgeHex & 255), "и темнее края светящегося");
            Assert.That(b, Is.GreaterThan(r + 40).And.GreaterThan(g + 30), "без тёплых тонов");
        }

        [Test]
        public void FlightStreak_IsAComet_HotAndWideAtTheHead_GoneAtTheGrip()
        {
            float nearHead = PelagAnchorThrowIronLook.FlightHalf(.3f, 6f);
            float middle = PelagAnchorThrowIronLook.FlightHalf(3f, 3f);
            float tail = PelagAnchorThrowIronLook.FlightHalf(4.4f, 1.5f);
            Assert.That(nearHead, Is.GreaterThan(middle));
            Assert.That(middle, Is.GreaterThan(tail));
            Assert.That(PelagAnchorThrowIronLook.FlightHalf(2f, 0f), Is.EqualTo(0f).Within(1e-6f), "у хвата сходит на нет");
            Assert.That(PelagAnchorThrowIronLook.FlightHalf(0f, 6f), Is.EqualTo(0f).Within(1e-6f), "кольцо закрывает голова");
            Assert.That(PelagAnchorThrowIronLook.FlightHeat(0f), Is.GreaterThan(PelagAnchorThrowIronLook.FlightHeat(4f)));
        }

        [Test]
        public void Tighten_HoldToYank_ThenAnEvenString()
        {
            Assert.That(PelagAnchorThrowIronLook.Tighten(110f, 112, 113), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowIronLook.Tighten(112.5f, 112, 113), Is.InRange(.2f, .8f));
            Assert.That(PelagAnchorThrowIronLook.Tighten(113f, 112, 113), Is.EqualTo(1f).Within(1e-5f));
            float h = PelagAnchorThrowIronLook.HalfAt(1f, 3f, 1f, 0f), h2 = PelagAnchorThrowIronLook.HalfAt(4f, 2f, 1f, 0f);
            Assert.That(h, Is.EqualTo(h2).Within(1e-4f), "натянутая струна ровная");
        }

        [Test]
        public void YankPulse_RunsFromTheHeadToTheHand_AndFades()
        {
            float early = Peak(.02f), later = Peak(.07f);
            Assert.That(later, Is.GreaterThan(early + 2f), "пятно света уходит к руке");
            Assert.That(PelagAnchorThrowIronLook.Pulse(3f, -.01f), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowIronLook.Pulse(PelagAnchorThrowIronLook.PulseSpeed * .25f, .25f), Is.EqualTo(0f).Within(1e-4f));
        }

        private static float Peak(float since)
        {
            float best = 0f, at = 0f;
            for (float m = 0f; m < 8f; m += .05f)
            {
                float v = PelagAnchorThrowIronLook.Pulse(m, since);
                if (v > best) { best = v; at = m; }
            }
            return at;
        }

        [Test]
        public void Chevrons_OnlyBetweenYankAndCatch_DenserNearTheHand()
        {
            Assert.That(PelagAnchorThrowIronLook.Chevrons(112.9f, 113, 119), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowIronLook.Chevrons(115f, 113, 119), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(PelagAnchorThrowIronLook.Chevrons(119.5f, 113, 119), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(PelagAnchorThrowIronLook.ChevronAt(0f), Is.EqualTo(PelagAnchorThrowIronLook.ChevronFrom).Within(1e-5f));
            Assert.That(PelagAnchorThrowIronLook.ChevronAt(1f), Is.EqualTo(PelagAnchorThrowIronLook.ChevronTo).Within(1e-5f));
            float mid = (PelagAnchorThrowIronLook.ChevronFrom + PelagAnchorThrowIronLook.ChevronTo) * .5f;
            Assert.That(PelagAnchorThrowIronLook.ChevronAt(.5f), Is.GreaterThan(mid), "у рук гуще (кадр рывка)");
        }

        [Test]
        public void Furrow_BodyWide_RoundStart_NoseAtTheBody()
        {
            Assert.That(PelagAnchorThrowIronLook.FurrowHalf(.3f), Is.EqualTo(.28f).Within(1e-5f));
            Assert.That(PelagAnchorThrowIronLook.FurrowHalf(.55f), Is.InRange(.40f, .55f));
            Assert.That(PelagAnchorThrowIronLook.FurrowWidthAt(0f, 3f), Is.EqualTo(0f));
            Assert.That(PelagAnchorThrowIronLook.FurrowWidthAt(1.5f, 1.5f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(PelagAnchorThrowIronLook.FurrowWidthAt(1.5f, 0f), Is.InRange(.3f, .8f), "нос у тела уже");
        }

        [Test]
        public void Hits_HeavyBiggerThanLight_SpendCountsByMetres()
        {
            Assert.That(PelagAnchorThrowIronLook.HitScale(false, false), Is.GreaterThan(PelagAnchorThrowIronLook.HitScale(true, false)));
            Assert.That(PelagAnchorThrowIronLook.HitScale(true, false), Is.GreaterThan(PelagAnchorThrowIronLook.HitScale(true, true)));
            float carry = 0f;
            int total = 0;
            for (int i = 0; i < 20; i++) total += PelagAnchorThrowIronLook.Spend(ref carry, .1f, PelagAnchorThrowIronLook.RockEvery);
            Assert.That(total, Is.EqualTo((int)(2f / PelagAnchorThrowIronLook.RockEvery)));
        }
    }
}
