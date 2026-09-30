using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Засечки на полосе босса (выбор владельца 30.09, кадр 1a + подпись «Босс · фаза 2» из 1b): где они стоят,
    /// когда загораются и какая сейчас фаза. Чистая логика без Unity — проверяется в
    /// Combat.Presentation.Tests (RunHudPolishTests); вид — RunHud.Boss, узлы — миграция v3 RunHudWcBuilder.
    ///
    /// Засечек три, слева направо по заливке — 33%, 50%, 66%; по порядку прохождения — 66% (первая подмога),
    /// 50% (ярость), 33% (вторая подмога). Пороги подмоги — те же, что у симуляции
    /// (Simulation.BossAddFirstPercent/SecondPercent, сравнение «здоровье·100 ≤ макс·процент»), ярость —
    /// как у RiftRun (здоровье ≤ половины). Фаза считает только подмогу: «фаза 2» — после первой, «фаза 3» —
    /// после второй; ярость дописывается к подписи отдельным словом.
    /// </summary>
    public static class RunHudBossMarks
    {
        /// <summary>Что случается на засечке: зовёт подмогу или впадает в ярость.</summary>
        public enum Kind : byte { Adds, Rage }

        /// <summary>Засечек на полосе.</summary>
        public const int Count = 3;

        /// <summary>Ярость босса — на половине здоровья (RiftRun: Health ≤ MaxHealth / 2).</summary>
        public const int RagePercent = 50;

        /// <summary>Процент засечки <paramref name="index"/> в порядке прохождения: 66, 50, 33.</summary>
        public static int PercentAt(int index)
            => index == 0 ? Simulation.BossAddFirstPercent : index == 1 ? RagePercent : Simulation.BossAddSecondPercent;

        public static Kind KindAt(int index) => index == 1 ? Kind.Rage : Kind.Adds;

        /// <summary>Место засечки на полосе: доля её ширины от левого края.</summary>
        public static float Fraction(int index) => PercentAt(index) / 100f;

        /// <summary>Порог пройден: то же сравнение, что у симуляции, без дробей.</summary>
        public static bool Crossed(int health, int maxHealth, int percent)
            => maxHealth > 0 && (long)health * 100 <= (long)maxHealth * percent;

        /// <summary>Засечка <paramref name="index"/> уже пройдена при этом здоровье.</summary>
        public static bool Passed(int index, int health, int maxHealth) => Crossed(health, maxHealth, PercentAt(index));

        /// <summary>Фаза боя: 1 — до подмоги, 2 — после первой, 3 — после второй.</summary>
        public static int Phase(int health, int maxHealth)
            => 1 + (Crossed(health, maxHealth, Simulation.BossAddFirstPercent) ? 1 : 0)
                 + (Crossed(health, maxHealth, Simulation.BossAddSecondPercent) ? 1 : 0);

        /// <summary>Подпись под именем: «Босс», «Босс · фаза 2», «Босс · фаза 2 · ярость».</summary>
        public static string Subtitle(int phase, bool enraged)
            => "Босс" + (phase > 1 ? " · фаза " + phase : "") + (enraged ? " · ярость" : "");

        /// <summary>
        /// Появление босса: имя пишется тушью, затем полоса наливается до его здоровья. Доля показа
        /// через <paramref name="since"/> секунд после появления — от 0 до <paramref name="value"/>.
        /// </summary>
        public static float IntroFill(float value, float since)
        {
            if (since <= IntroFillDelay) return 0f;
            float k = (since - IntroFillDelay) / IntroFillDuration;
            if (k >= 1f) return value;
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            return value * eased;
        }

        /// <summary>Полоса начинает наливаться, когда имя уже проступило.</summary>
        public const float IntroFillDelay = .35f;
        public const float IntroFillDuration = .9f;

        /// <summary>Появление кончилось: полоса дальше показывает здоровье как есть.</summary>
        public static bool IntroDone(float since) => since >= IntroFillDelay + IntroFillDuration;
    }
}
