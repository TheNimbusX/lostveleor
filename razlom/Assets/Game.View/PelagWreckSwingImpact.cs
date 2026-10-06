using System;

namespace Game.View
{
    /// <summary>
    /// Знак маха Крушения на задетом, вид V2 (06.10; swing1.png / swing2.png): на каждый Damage маха — короткий
    /// росчерк ПО ХОДУ маха (растянутая частица пака CFXR «stretch trait» в цвет формы, под ним тёмный контур тем же
    /// листом, сердцевина к белому), сколы железа (лист обломков CFXR «debris unlit 3x3», перекрашен: тёмное железо с
    /// голубой кромкой формы), клуб пыли у ног и отброс тела (только вид — Sim махом не толкает). ШАРА НЕТ (V1 рисовал
    /// круглую голубую вспышку). Мах 2 и «Четвёртый удар» — тяжелее: длиннее росчерк, больше и крупнее сколы.
    /// Без Unity — тесты tools/Combat.Presentation.Tests/WreckSwingRulesTests.cs.
    /// </summary>
    public static class PelagWreckSwingImpact
    {
        public struct Numbers
        {
            /// <summary>Росчерк: видимая длина и толщина, м; жизнь, с; сдвиг по ходу за жизнь, м; тонких сбоку.</summary>
            public float StreakLength, StreakWidth, StreakSeconds, StreakTravel;
            public int StreakSlivers;
            /// <summary>Сколы железа: сколько, размер, м; скорость, м/с; жизнь, с.</summary>
            public int Chips;
            public float ChipSizeMin, ChipSizeMax, ChipSpeedMin, ChipSpeedMax, ChipSecondsMin, ChipSecondsMax;
            /// <summary>Пыль у ног: клубов, размер, м; жизнь, с.</summary>
            public int Dust;
            public float DustSizeMin, DustSizeMax, DustSeconds;
            /// <summary>Отброс тела: сдвиг, м (рывок и возврат за PelagWreckVfxRules.RecoilSeconds).</summary>
            public float RecoilMeters;
            /// <summary>Толчок камеры в удар маха (сила, приближение).</summary>
            public float PunchTrauma, PunchZoom;
            /// <summary>Искры с головы якоря: вспышкой в удар; струёй, пока голова хлещет, штук/с.</summary>
            public int ContactGlints;
            public float GlintRate;
        }

        private static readonly Numbers LightNumbers = new Numbers
        {
            StreakLength = .9f, StreakWidth = .16f, StreakSeconds = .09f, StreakTravel = .35f, StreakSlivers = 1,
            Chips = 4, ChipSizeMin = .07f, ChipSizeMax = .12f, ChipSpeedMin = 3f, ChipSpeedMax = 5.5f, ChipSecondsMin = .35f, ChipSecondsMax = .5f,
            Dust = 2, DustSizeMin = .5f, DustSizeMax = .7f, DustSeconds = .55f,
            RecoilMeters = .2f, PunchTrauma = .08f, PunchZoom = .03f,
            ContactGlints = 5, GlintRate = 30f
        };

        private static readonly Numbers HeavyNumbers = new Numbers
        {
            StreakLength = 1.15f, StreakWidth = .2f, StreakSeconds = .11f, StreakTravel = .45f, StreakSlivers = 2,
            Chips = 6, ChipSizeMin = .09f, ChipSizeMax = .15f, ChipSpeedMin = 3.5f, ChipSpeedMax = 6.5f, ChipSecondsMin = .4f, ChipSecondsMax = .55f,
            Dust = 3, DustSizeMin = .65f, DustSizeMax = .9f, DustSeconds = .65f,
            RecoilMeters = .32f, PunchTrauma = .12f, PunchZoom = .04f,
            ContactGlints = 8, GlintRate = 44f
        };

        public static Numbers For(WreckSwingWeight weight)
            => weight == WreckSwingWeight.Heavy ? HeavyNumbers : LightNumbers;

        /// <summary>
        /// Растянутая частица: длина = размер × StreakAspect (lengthScale рендера, скорость не тянет — velocityScale 0).
        /// Сборка ставит его рендеру, вид отдаёт размер через <see cref="StreakStartSize"/>.
        /// </summary>
        public const float StreakAspect = 5.6f;

        public static float StreakStartSize(float visibleLength) => visibleLength / StreakAspect;

        /// <summary>Тёмный контур под росчерком — шире на эту долю; сердцевина — эта доля толщины.</summary>
        public const float InkGrow = .45f, CoreShare = .45f;

        /// <summary>Тонкие росчерки сбоку: отклонение от хода, градусы; сдвиг поперёк, м; доля длины.</summary>
        public const float SliverDegrees = 12f, SliverOffset = .12f, SliverLength = .6f;

        /// <summary>Скорость росчерка по ходу, м/с: проходит StreakTravel за жизнь.</summary>
        public static float StreakSpeed(in Numbers n) => n.StreakTravel / Math.Max(1e-3f, n.StreakSeconds);

        /// <summary>Множитель направления отдачи вида (WkBody.RecoilDir): отдача Крушения v2 — PelagWreckVfxRules.RecoilMeters.</summary>
        public static float RecoilScale(WreckSwingWeight weight)
            => For(weight).RecoilMeters / Math.Max(1e-3f, PelagWreckVfxRules.RecoilMeters);
    }
}
