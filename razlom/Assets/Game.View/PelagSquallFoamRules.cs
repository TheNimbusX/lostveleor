using System;

namespace Game.View
{
    /// <summary>
    /// Числа вида Шквала v2 без Unity — проверяются тестами
    /// (tools/Combat.Presentation.Tests/SquallFoamViewRulesTests.cs):
    ///  • когда рвётся вода полосы Пенного следа (последние капли — до конца полосы в Sim,
    ///    до того — ни трещин, ни побеления);
    ///  • ход воды без излома скорости, когда струя становится полосой;
    ///  • последний ли удар (Охота за добивание даёт лишний прыжок);
    ///  • размер кольца пены по телу цели;
    ///  • цвет капель принятых префабов семьи в форме Шквала.
    /// </summary>
    public static class PelagSquallFoamRules
    {
        /// <summary>_Break.x материала M_Squall_Water: на этом возрасте распада вода рвётся.</summary>
        public const float ShaderBreakAge = .30f;
        /// <summary>_Break.y / 2: разброс трещин по шуму, ±с.</summary>
        public const float ShaderBreakJitter = .03f;
        /// <summary>_DropLife материала: капли живут после трещин, с.</summary>
        public const float ShaderDropLife = .07f;
        /// <summary>_Break.w материала: вода белеет пеной до трещин, с (в шкале возраста распада).</summary>
        public const float ShaderFoamUp = .12f;

        /// <summary>Хвост струи старше головы: распад фронтом от старта к ногам, с.</summary>
        public const float StreakTailLead = .10f;

        /// <summary>
        /// Конец полосы: голова трескается за TrailEndLead до конца полосы в Sim, хвост — ещё
        /// на TrailTailLead раньше; возраст распада в конце идёт на TrailEndRate от
        /// настоящего времени — побеление ~0,22 с, капли ~0,13 с (мягче струи).
        /// </summary>
        public const float TrailTailLead = .16f, TrailEndLead = .20f, TrailEndRate = .55f;

        /// <summary>Ход воды вдоль пути, м/с: рывок на рождении, к покою — экспонентой.</summary>
        public const float FlowFast = 2.0f, FlowCalm = .35f, FlowEase = .35f;

        /// <summary>Кольца пены у ног: авторский радиус префаба и запас над телом, м; пределы масштаба.</summary>
        public const float RingAuthored = .5f, RingSlack = .15f, RingMinScale = .8f, RingMaxScale = 1.9f;

        /// <summary>
        /// Возраст распада участка полосы: <paramref name="remaining"/> — секунд до конца
        /// полосы в Sim, <paramref name="k"/> — 0 у старта прыжка … 1 у посадки.
        /// </summary>
        public static float TrailBreakAge(float remaining, float k)
        {
            k = Clamp01(k);
            return Math.Max(0f, ShaderBreakAge - (remaining - TrailEndLead - (1f - k) * TrailTailLead) * TrailEndRate);
        }

        /// <summary>За сколько секунд до конца полосы участок <paramref name="k"/> доходит до возраста <paramref name="age"/>.</summary>
        public static float TrailRemainingAt(float age, float k)
            => TrailEndLead + (1f - Clamp01(k)) * TrailTailLead + (ShaderBreakAge - age) / TrailEndRate;

        /// <summary>Сдвиг рисунка воды к старту, м: у струи — ровный рывок, у полосы — замедление после привязки.</summary>
        public static float Flow(bool bound, float age, float boundAt)
        {
            if (!bound) return FlowFast * age;
            float t = Math.Max(0f, age - boundAt);
            return FlowFast * boundAt + FlowCalm * t + (FlowFast - FlowCalm) * FlowEase * (1f - (float)Math.Exp(-t / FlowEase));
        }

        /// <summary>Скорость хода воды, м/с (производная Flow).</summary>
        public static float FlowSpeed(bool bound, float age, float boundAt)
        {
            if (!bound) return FlowFast;
            float t = Math.Max(0f, age - boundAt);
            return FlowCalm + (FlowFast - FlowCalm) * (float)Math.Exp(-t / FlowEase);
        }

        /// <summary>
        /// Последний удар серии: прыжков не осталось (SquallStrike.Amount ≤ 0) и этот удар
        /// не дал лишнего прыжка Охоты (SquallHuntKill с тем же номером прыжка).
        /// </summary>
        public static bool IsFinalStrike(int jumpsLeft, int strikeIndex, int huntKillIndex)
            => jumpsLeft <= 0 && huntKillIndex != strikeIndex;

        /// <summary>Масштаб кольца пены у ног цели по радиусу её тела, м.</summary>
        public static float RingScale(float bodyRadius)
        {
            float scale = (bodyRadius + RingSlack) / RingAuthored;
            return scale < RingMinScale ? RingMinScale : scale > RingMaxScale ? RingMaxScale : scale;
        }

        /// <summary>
        /// Бирюзовый отлив, которым целиком окрашена капля семьи DropAqua (.50; .92; .95):
        /// (g + b) / 2 − r. Белые капли и комья (1,08; 1,18; 1,18) — с отливом ~0,23, чистый белый — 0.
        /// </summary>
        public const float DropAquaCast = .435f;

        /// <summary>Доля бирюзового отлива цвета частицы: 0 — белый или тёплый, 1 — капля DropAqua и бирюзовее.</summary>
        public static float DropCast(float r, float g, float b) => Clamp01(((g + b) * .5f - r) / DropAquaCast);

        /// <summary>
        /// Цвет частицы принятого префаба семьи (всплеск сабли, корона рывка, волна-толчок) в форме
        /// Шквала: тело капли — цвет частицы (шейдер Razlom/Sabre Foam Blob, _Shade красит лишь
        /// кромку), поэтому её бирюзовый отлив сдвигается на разницу Shallow формы и базы
        /// (shift*) в доле DropCast. Бирюзовая капля становится цветом формы, белый остаётся белым
        /// с отливом формы вместо бирюзового; база (сдвиг 0) — авторский цвет.
        /// </summary>
        public static void RecastDrop(ref float r, ref float g, ref float b, float shiftR, float shiftG, float shiftB)
        {
            float k = DropCast(r, g, b);
            r = Math.Max(0f, r + shiftR * k);
            g = Math.Max(0f, g + shiftG * k);
            b = Math.Max(0f, b + shiftB * k);
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
