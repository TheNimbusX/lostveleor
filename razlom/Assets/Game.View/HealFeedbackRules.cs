using System;
using System.Globalization;

namespace Game.View
{
    /// <summary>
    /// Зелёный отрезок лечения на полоске одного врага: откуда он начинается, откуда растёт
    /// фронт этого лечения, докуда, и сколько секунд прошло.
    /// </summary>
    public struct BarHealState
    {
        /// <summary>Начало зелёного отрезка — доля здоровья до лечения (у серии — до первого).</summary>
        public float From;

        /// <summary>Откуда растёт фронт этого лечения: у второго подряд — с того места, где фронт стоял.</summary>
        public float GrowFrom;

        /// <summary>Доля здоровья после лечения — сюда дорастает фронт.</summary>
        public float To;

        /// <summary>Секунд с начала этого лечения.</summary>
        public float Age;

        public bool Active;
    }

    /// <summary>
    /// ОТКЛИК ЛЕЧЕНИЯ НА ПОЛОСКЕ ВРАГА (ревью 01.10, Корнехват: «когда он хилит союзников,
    /// нет эффекта, показывающего, что полоска здоровья чуть восстановилась»).
    ///
    /// Заливка не прыгает на новое здоровье разом: от прежнего конца заливки вырастает
    /// зелёный отрезок (GrowSeconds, с торможением), на приходе — короткая яркая вспышка
    /// (Flash), отрезок держится и тает в обычный цвет полоски (Hold, Fade). Урон посреди
    /// лечения срезает отрезок сверху — полоска не врёт о здоровье дольше, чем растёт фронт.
    /// Без UnityEngine — проверяется тестами вне Unity
    /// (tools/Combat.Presentation.Tests/HealFeedbackTests.cs).
    /// </summary>
    public static class HealBarFeedback
    {
        /// <summary>Зелёный отрезок дорастает до нового здоровья, с.</summary>
        public const float GrowSeconds = .32f;

        /// <summary>Держится полным зелёным после прихода, с.</summary>
        public const float HoldSeconds = .55f;

        /// <summary>Тает в обычный цвет полоски, с.</summary>
        public const float FadeSeconds = .45f;

        /// <summary>Вспышка на приходе фронта гаснет за столько, с.</summary>
        public const float FlashSeconds = .28f;

        public const float TotalSeconds = GrowSeconds + HoldSeconds + FadeSeconds;

        /// <summary>
        /// Новое лечение: доля до (<paramref name="before"/>) и после (<paramref name="after"/>).
        /// Если прошлый отрезок ещё виден — серия: зелёное начинается с прежнего начала, а фронт
        /// растёт с того места, где стоял, без отката назад. Лечение «в ноль» ничего не начинает.
        /// </summary>
        public static void Begin(ref BarHealState s, float before, float after)
        {
            before = Clamp01(before);
            after = Clamp01(after);
            if (!(after > before + 1e-4f)) return;
            if (s.Active && s.Age < TotalSeconds)
            {
                float front = Front(in s);
                s.GrowFrom = Math.Min(front, before);
                s.From = Math.Min(s.From, s.GrowFrom);
            }
            else
            {
                s.From = before;
                s.GrowFrom = before;
            }
            s.To = after;
            s.Age = 0f;
            s.Active = true;
        }

        /// <summary>Шаг времени; отрезок кончился — состояние гаснет.</summary>
        public static void Step(ref BarHealState s, float dt)
        {
            if (!s.Active) return;
            s.Age += Math.Max(0f, dt);
            if (s.Age >= TotalSeconds) s.Active = false;
        }

        /// <summary>Где сейчас фронт: от GrowFrom к To с торможением (кубический ease-out).</summary>
        public static float Front(in BarHealState s)
        {
            if (!s.Active) return s.To;
            float k = Clamp01(s.Age / GrowSeconds);
            float u = 1f - k;
            return s.GrowFrom + (s.To - s.GrowFrom) * (1f - u * u * u);
        }

        /// <summary>
        /// Доля, которую рисует заливка: пока фронт растёт — не дальше фронта (заливка растёт
        /// вместе с зелёным), потом — настоящее здоровье.
        /// </summary>
        public static float ShownFill(in BarHealState s, float actual)
        {
            actual = Clamp01(actual);
            if (!s.Active || s.Age >= GrowSeconds) return actual;
            return Math.Min(actual, Front(in s));
        }

        /// <summary>
        /// Зелёный отрезок в долях полоски: от начала лечения до фронта, обрезанный настоящим
        /// здоровьем (урон посреди лечения съедает отрезок сверху). false — рисовать нечего.
        /// </summary>
        public static bool Segment(in BarHealState s, float actual, out float from, out float to)
        {
            actual = Clamp01(actual);
            from = Math.Min(s.From, actual);
            to = Math.Min(Front(in s), actual);
            return s.Active && Alpha(in s) > 0f && to - from > .002f;
        }

        /// <summary>Непрозрачность отрезка: полная, пока растёт и держится, потом мягко в ноль.</summary>
        public static float Alpha(in BarHealState s)
        {
            if (!s.Active) return 0f;
            float fading = s.Age - GrowSeconds - HoldSeconds;
            if (fading <= 0f) return 1f;
            return 1f - HeroHitFeedbackCurves.Smooth(fading / FadeSeconds);
        }

        /// <summary>
        /// Яркость вспышки 0..1: разгорается, пока фронт растёт (пик — в миг прихода), и гаснет
        /// за FlashSeconds. Вне лечения — ноль.
        /// </summary>
        public static float Flash(in BarHealState s)
        {
            if (!s.Active) return 0f;
            if (s.Age < GrowSeconds)
            {
                float k = Clamp01(s.Age / GrowSeconds);
                return k * k;
            }
            float after = s.Age - GrowSeconds;
            if (after >= FlashSeconds) return 0f;
            return 1f - HeroHitFeedbackCurves.Smooth(after / FlashSeconds);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : float.IsNaN(v) ? 0f : v;
    }

    /// <summary>
    /// Зелёная цифра лечения «+N» над вылеченным союзником (ревью 01.10). С уроном не
    /// сливается никогда: это другой смысл. Две волны лечения подряд по одной цели в окне —
    /// одна растущая цифра, как серия ударов.
    /// </summary>
    public static class HealNumberRules
    {
        /// <summary>Окно слияния лечений одной цели, с.</summary>
        public const float MergeWindow = .6f;

        /// <summary>Текст цифры: «+15». Ноль и отрицательное — пусто (такой цифры нет).</summary>
        public static string Text(int amount)
            => amount > 0 ? "+" + amount.ToString(CultureInfo.InvariantCulture) : string.Empty;

        /// <summary>Вливается ли лечение в висящую «+N» той же цели: с прошлого прошло меньше окна.</summary>
        public static bool Merges(float sinceHeal, float window = MergeWindow)
            => sinceHeal >= 0f && sinceHeal < window;

        /// <summary>Сумма слитых лечений без переполнения; отрицательное не вычитается.</summary>
        public static int Add(int shown, int amount)
        {
            long sum = (long)Math.Max(0, shown) + Math.Max(0, amount);
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }
    }
}
